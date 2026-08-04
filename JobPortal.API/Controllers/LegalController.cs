using JobPortal.Application.Features.Legal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/legal")]
[Produces("application/json")]
public sealed class LegalController : ControllerBase
{
    [HttpGet("terms-of-use")]
    [ProducesResponseType(typeof(LegalDocumentResponse), StatusCodes.Status200OK)]
    public ActionResult<LegalDocumentResponse> TermsOfUse() =>
        Ok(LegalDocumentCatalog.TermsOfUse());

    [HttpGet("privacy-policy")]
    [ProducesResponseType(typeof(LegalDocumentResponse), StatusCodes.Status200OK)]
    public ActionResult<LegalDocumentResponse> PrivacyPolicy() =>
        Ok(LegalDocumentCatalog.PrivacyPolicy());
}
