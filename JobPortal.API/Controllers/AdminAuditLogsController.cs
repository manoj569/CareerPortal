using JobPortal.Application.Abstractions.Auditing;
using JobPortal.Application.Features.Auditing;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize(Roles = "Administrator")]
[Route("api/admin/audit-logs")]
[Produces("application/json")]
public sealed class AdminAuditLogsController(
    IAuditLogService auditLogs) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(
        typeof(ApiResponse<PagedResponse<AuditLogResponse>>),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AuditLogResponse>>>> Search(
        [FromQuery] AuditLogQuery query,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<AuditLogResponse>>(
            await auditLogs.SearchAsync(query, cancellationToken)));
}
