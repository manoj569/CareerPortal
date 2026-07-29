using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.AdminApplications;
using JobPortal.Application.Features.AdminApplications;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/applications")]
[Produces("application/json")]
public sealed class AdminApplicationsController(
    IAdminApplicationService applications) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminApplicationListItem>>>> Search(
        [FromQuery] AdminApplicationQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<AdminApplicationListItem>>(
            await applications.SearchAsync(query, cancellationToken)));

    [HttpGet("{applicationId:guid}")]
    public async Task<ActionResult<ApiResponse<AdminApplicationDetail>>> Get(
        Guid applicationId, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminApplicationDetail>(
            await applications.GetAsync(applicationId, cancellationToken)));

    [HttpGet("{applicationId:guid}/resume")]
    public async Task<IActionResult> DownloadResume(
        Guid applicationId, CancellationToken cancellationToken)
    {
        var result = await applications.DownloadResumeAsync(
            applicationId, cancellationToken);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpPut("{applicationId:guid}/status")]
    public async Task<ActionResult<ApiResponse<AdminApplicationDetail>>> UpdateStatus(
        Guid applicationId, [FromBody] UpdateAdminApplicationStatusRequest request,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<AdminApplicationDetail>(
            await applications.UpdateStatusAsync(
                User.GetRequiredUserId(), applicationId, request, cancellationToken),
            "Application status updated successfully."));
}
