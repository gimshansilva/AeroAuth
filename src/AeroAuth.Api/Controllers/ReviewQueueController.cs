using AeroAuth.Core.Constants;
using AeroAuth.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AeroAuth.Api.Controllers;

[ApiController, Route("api/review-queue"), Authorize(Roles = $"{Roles.AdminQA},{Roles.InternalQA}")]
public class ReviewQueueController(IReviewQueueRepository queue) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Pending() => Ok(await queue.GetPendingAsync());
}
