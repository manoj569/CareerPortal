using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Features.PublicJobs;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/jobs")]
[Produces("application/json")]
[OutputCache(PolicyName = "PublicJobs")]
public sealed class PublicJobsController(IPublicJobService jobService) : ControllerBase
{
    [HttpGet]
    [HttpGet("search")]
    [HttpGet("filter")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<PublicJobSummary>>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PagedResponse<PublicJobSummary>>>> Search(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query, cancellationToken);

    [HttpGet("latest")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<PublicJobSummary>>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PagedResponse<PublicJobSummary>>>> Latest(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { SortBy = "publishedAt", SortDirection = "desc" }, cancellationToken);

    [HttpGet("newest")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<PublicJobSummary>>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PagedResponse<PublicJobSummary>>>> Newest(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { SortBy = "createdAt", SortDirection = "desc" }, cancellationToken);

    [HttpGet("featured")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<PublicJobSummary>>), StatusCodes.Status200OK)]
    public Task<ActionResult<ApiResponse<PagedResponse<PublicJobSummary>>>> Featured(
        [FromQuery] PublicJobQuery query, CancellationToken cancellationToken) =>
        GetPageAsync(query with { IsFeatured = true }, cancellationToken);

    [HttpGet("companies/popular")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>>> PopularCompanies(
        [FromQuery] int limit = 10, CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<PopularCompanyResponse>>(
            await jobService.GetPopularCompaniesAsync(limit, cancellationToken)));

    [HttpGet("{slug}/related")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyCollection<PublicJobSummary>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<IReadOnlyCollection<PublicJobSummary>>>> Related(
        string slug, [FromQuery] int limit = 6, CancellationToken cancellationToken = default) =>
        Ok(new ApiResponse<IReadOnlyCollection<PublicJobSummary>>(
            await jobService.GetRelatedAsync(slug, limit, cancellationToken)));

    [HttpGet("{slug}")]
    [ProducesResponseType(typeof(ApiResponse<PublicJobDetails>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PublicJobDetails>>> Details(
        string slug, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PublicJobDetails>(
            await jobService.GetDetailsAsync(slug, cancellationToken)));

    private async Task<ActionResult<ApiResponse<PagedResponse<PublicJobSummary>>>> GetPageAsync(
        PublicJobQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<PublicJobSummary>>(
            await jobService.SearchAsync(query, cancellationToken)));
}
