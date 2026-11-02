using AeroAuth.Api.Models;
using AeroAuth.Core.Constants;
using AeroAuth.Core.Entities;
using AeroAuth.Core.Interfaces;
using AeroAuth.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroAuth.Api.Controllers;

[ApiController, Route("api/training-sessions"), Authorize]
public class TrainingSessionsController(ITrainingSessionRepository sessions, ICertificateRepository certificates,
                                        TrainingApprovalService approval, ITenantContext me) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status) => Ok(await sessions.ListAsync(status));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var s = await sessions.GetByIdAsync(id);
        if (s is null) return NotFound();
        return Ok(new { session = s, attendees = await sessions.GetAttendeesAsync(id), certificates = await certificates.ListBySessionAsync(id) });
    }

    [HttpPost, Authorize(Roles = $"{Roles.AdminQA},{Roles.ExternalAMO}")]
    public async Task<IActionResult> Submit(SubmitSessionRequest r)
    {
        var organizationId = me.IsExternalAmo ? me.OrganizationId : r.OrganizationId;
        if (organizationId is null) return BadRequest(new { error = "OrganizationId is required." });

        var id = await approval.SubmitAsync(new TrainingSession
        {
            OrganizationId = organizationId.Value, StationId = r.StationId, TrainerStaffId = r.TrainerStaffId, SessionDate = r.SessionDate,
            Venue = r.Venue, AttendanceSheetPath = r.AttendanceSheetPath,
            Modules = r.MaterialIds.Distinct().Select(m => new SessionModule { MaterialId = m }).ToList(),
            Attendees = r.AttendeeStaffIds.Distinct().Select(s => new SessionAttendee { StaffId = s }).ToList()
        });
        return CreatedAtAction(nameof(Get), new { id }, new { sessionId = id, status = ReviewStatus.Pending });
    }

    /// <summary>Approve: queue item, session status, certificate records and counters change in one transaction.</summary>
    [HttpPost("{id:int}/approve"), Authorize(Roles = Roles.AdminQA)]
    public async Task<IActionResult> Approve(int id, NotesRequest r) => Ok(await approval.ApproveAsync(id, r.Notes));

    [HttpPost("{id:int}/reject"), Authorize(Roles = Roles.AdminQA)]
    public async Task<IActionResult> Reject(int id, DecisionRequest r)
    {
        await approval.RejectAsync(id, r.Comment ?? "");
        return NoContent();
    }
}
