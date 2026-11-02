using AeroAuth.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroAuth.Api.Controllers;

[ApiController, Route("api/reference"), Authorize]
public class ReferenceController(IReferenceRepository reference) : ControllerBase
{
    [HttpGet("aircraft-types")] public async Task<IActionResult> AircraftTypes() => Ok(await reference.GetAircraftTypesAsync());
    [HttpGet("issuing-naas")]   public async Task<IActionResult> IssuingNaas() => Ok(await reference.GetIssuingNaasAsync());
    [HttpGet("freeze-reasons")] public async Task<IActionResult> FreezeReasons() => Ok(await reference.GetFreezeReasonsAsync());
}
