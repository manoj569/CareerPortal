using JobPortal.API.Extensions;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.API.Controllers;

[ApiController]
[Authorize]
[Route("api/payments")]
[Produces("application/json")]
public sealed class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpPost("razorpay/orders")]
    [ProducesResponseType(typeof(ApiResponse<PaymentOrderResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<ApiResponse<PaymentOrderResponse>>> CreateOrder(
        [FromBody] CreatePaymentOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await paymentService.CreateOrderAsync(User.GetRequiredUserId(), request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created,
            new ApiResponse<PaymentOrderResponse>(result, "Razorpay order created."));
    }

    [HttpPost("{paymentId:guid}/razorpay/confirm")]
    public async Task<ActionResult<ApiResponse<PaymentResponse>>> Confirm(
        Guid paymentId, [FromBody] ConfirmRazorpayPaymentRequest request,
        CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PaymentResponse>(
            await paymentService.ConfirmAsync(User.GetRequiredUserId(), paymentId, request, cancellationToken),
            "Payment confirmed and membership activated."));

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResponse<PaymentResponse>>>> Payments(
        [FromQuery] HistoryQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<PaymentResponse>>(
            await paymentService.GetPaymentsAsync(User.GetRequiredUserId(), query, cancellationToken)));

    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<PagedResponse<PaymentHistoryResponse>>>> History(
        [FromQuery] HistoryQuery query, CancellationToken cancellationToken) =>
        Ok(new ApiResponse<PagedResponse<PaymentHistoryResponse>>(
            await paymentService.GetHistoryAsync(User.GetRequiredUserId(), query, cancellationToken)));
}
