using AeroAuth.Api.Models;
using AeroAuth.Core.Constants;
using AeroAuth.Core.Entities;
using AeroAuth.Core.Interfaces;
using AeroAuth.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroAuth.Api.Controllers;

/// <summary>
/// Staff endpoints. Tenant isolation is applied in the repository from the token, so a request can never reach another
/// organization's staff: a record of another organization simply is not found (404).
/// </summary>
[ApiController, Route("api/staff"), Authorize]
public class StaffController(IStaffRepository staff, StaffService staffService, ITenantContext me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? search, [FromQuery] string? status, [FromQuery] bool? frozen, [FromQuery] int? stationId) =>
        Ok(await staff.SearchAsync(new StaffFilter { Search = search, ReviewStatus = status, IsFrozen = frozen, StationId = stationId }));

    /// <summary>One staff member with licenses and scope (single query, Dapper multi-mapping).</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var s = await staff.GetWithDetailsAsync(id);
        return s is null ? NotFound() : Ok(s);
    }

    /// <summary>Submits a new staff member. Saved as Pending and queued for QA review in one transaction.</summary>
    [HttpPost, Authorize(Roles = $"{Roles.AdminQA},{Roles.ExternalAMO}")]
    public async Task<IActionResult> Submit(SubmitStaffRequest r)
    {
        var organizationId = me.IsExternalAmo ? me.OrganizationId : r.OrganizationId;   // an AMO user cannot choose another organization
        if (organizationId is null) return BadRequest(new { error = "OrganizationId is required." });

        var newStaff = new Staff
        {
            OrganizationId = organizationId.Value, StationId = r.StationId, StaffNumber = r.StaffNumber.Trim(),
            FullName = r.FullName.Trim(), Email = r.Email
        };
        var amls = r.Aml.Select(a => new StaffAml { NaaId = a.NaaId, AmlNumber = a.AmlNumber.Trim(), IssueDate = a.IssueDate, ExpiryDate = a.ExpiryDate }).ToList();
        var scopes = r.Scope.Select(s => (new StaffScope
        {
            AircraftTypeId = s.AircraftTypeId, ScopeBasis = s.ScopeBasis, Category = s.Category.Trim(), Level = s.Level.Trim(),
            Location = s.Location, EffectiveFrom = s.EffectiveFrom, EffectiveTo = s.EffectiveTo
        }, s.AmlIndex)).ToList();

        var id = await staffService.SubmitAsync(newStaff, amls, scopes);
        return CreatedAtAction(nameof(Get), new { id }, new { staffId = id, reviewStatus = ReviewStatus.Pending });
    }

    [HttpPut("{id:int}"), Authorize(Roles = $"{Roles.AdminQA},{Roles.ExternalAMO}")]
    public async Task<IActionResult> Update(int id, UpdateStaffRequest r)
    {
        var updated = await staff.UpdateAsync(new Staff
        {
            StaffId = id, StationId = r.StationId, FullName = r.FullName.Trim(), Email = r.Email,
            SignaturePath = r.SignaturePath, StampPath = r.StampPath, IsActive = r.IsActive, IsTrainer = r.IsTrainer
        });
        return updated ? NoContent() : NotFound();
    }

    /// <summary>Soft delete: the staff member is deactivated and stays on file.</summary>
    [HttpDelete("{id:int}"), Authorize(Roles = $"{Roles.AdminQA},{Roles.ExternalAMO}")]
    public async Task<IActionResult> Deactivate(int id) => await staff.DeactivateAsync(id) ? NoContent() : NotFound();

    [HttpPut("{id:int}/review"), Authorize(Roles = Roles.AdminQA)]
    public async Task<IActionResult> Decide(int id, DecisionRequest r)
    {
        await staffService.DecideAsync(id, r.Decision, r.Comment);
        return NoContent();
    }

    [HttpPost("{id:int}/freeze"), Authorize(Roles = Roles.AdminQA)]
    public async Task<IActionResult> Freeze(int id, FreezeRequest r)
    {
        await staffService.FreezeAsync(id, r.FreezeReasonId, r.Notes);
        return NoContent();
    }

    [HttpPost("{id:int}/unfreeze"), Authorize(Roles = Roles.AdminQA)]
    public async Task<IActionResult> Unfreeze(int id, NotesRequest r)
    {
        await staffService.UnfreezeAsync(id, r.Notes);
        return NoContent();
    }
}
