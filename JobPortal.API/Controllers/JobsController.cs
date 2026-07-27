using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.Jobs;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/jobs")]
[Produces("application/json")]
public sealed class JobsController(IJobService jobService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<JobResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<JobResponse>>>> Search(
        [FromQuery] JobSearchQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<JobResponse>>(await jobService.SearchAsync(query, cancellationToken)));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<JobResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<JobResponse>>> GetById(Guid id, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobResponse>(await jobService.GetByIdAsync(id, cancellationToken)));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<JobResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<JobResponse>>> Create(
        [FromBody] CreateJobRequest request, CancellationToken cancellationToken)
    {
        var job = await jobService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = job.Id },
            new ApiResponse<JobResponse>(job, "Job created successfully."));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<JobResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<JobResponse>>> Update(
        Guid id, [FromBody] UpdateJobRequest request, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<JobResponse>(await jobService.UpdateAsync(id, request, cancellationToken),
            "Job updated successfully."));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken cancellationToken)
    {
        await jobService.SoftDeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/permanent")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePermanently(Guid id, CancellationToken cancellationToken)
    {
        await jobService.DeletePermanentlyAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/publish")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Publish(Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(() => jobService.PublishAsync(id, cancellationToken), "Job published successfully.");

    [HttpPost("{id:guid}/archive")]
    public Task<ActionResult<ApiResponse<JobResponse>>> Archive(Guid id, CancellationToken cancellationToken) =>
        ExecuteStateChange(() => jobService.ArchiveAsync(id, cancellationToken), "Job archived successfully.");

    [HttpPut("{id:guid}/featured")]
    public Task<ActionResult<ApiResponse<JobResponse>>> SetFeatured(
        Guid id, [FromBody] SetJobFlagRequest request, CancellationToken cancellationToken) =>
        ExecuteStateChange(() => jobService.SetFeaturedAsync(id, request.Value, cancellationToken),
            request.Value ? "Job featured successfully." : "Job unfeatured successfully.");

    [HttpPut("{id:guid}/hidden")]
    public Task<ActionResult<ApiResponse<JobResponse>>> SetHidden(
        Guid id, [FromBody] SetJobFlagRequest request, CancellationToken cancellationToken) =>
        ExecuteStateChange(() => jobService.SetHiddenAsync(id, request.Value, cancellationToken),
            request.Value ? "Job hidden successfully." : "Job made visible successfully.");

    private async Task<ActionResult<ApiResponse<JobResponse>>> ExecuteStateChange(
        Func<Task<JobResponse>> operation, string message) =>
        Ok(new ApiResponse<JobResponse>(await operation(), message));
}

public sealed record SetJobFlagRequest(bool Value);
