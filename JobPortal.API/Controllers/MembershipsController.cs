using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Memberships;
using JobPortal.Application.Features.Memberships;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Route("api/memberships")]
[Produces("application/json")]
public sealed class MembershipsController(IMembershipService membershipService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("jobs/{slug}/apply")]
    [ProducesResponseType(typeof(ApiResponse<ApplicationAccessResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ApplicationAccessResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<ApplicationAccessResponse>), StatusCodes.Status402PaymentRequired)]
    public async Task<ActionResult<ApiResponse<ApplicationAccessResponse>>> Apply(
        string slug, CancellationToken cancellationToken)
    {
        var result = await membershipService.GetApplicationAccessAsync(
            User.TryGetUserId(), slug, cancellationToken);
        var response = new ApiResponse<ApplicationAccessResponse>(result);
        return result.Status switch
        {
            ApplicationAccessStatus.LoginRequired => StatusCode(StatusCodes.Status401Unauthorized, response),
            ApplicationAccessStatus.PaymentRequired => StatusCode(StatusCodes.Status402PaymentRequired, response),
            _ => Ok(response)
        };
    }

    [Authorize]
    [HttpGet("mine")]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<MembershipResponse>>>> Mine(
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<IReadOnlyCollection<MembershipResponse>>(
            await membershipService.GetMyMembershipsAsync(User.GetRequiredUserId(), cancellationToken)));

    [Authorize]
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<PagedResponse<MembershipHistoryResponse>>>> History(
        [FromQuery] HistoryQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<MembershipHistoryResponse>>(
            await membershipService.GetHistoryAsync(User.GetRequiredUserId(), query, cancellationToken)));
}
