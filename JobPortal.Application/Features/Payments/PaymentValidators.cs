using FluentValidation;

namespace JobPortal.Application.Features.Payments;

public sealed class CreatePaymentOrderRequestValidator : AbstractValidator<CreatePaymentOrderRequest>
{
}

public sealed class ConfirmRazorpayPaymentRequestValidator : AbstractValidator<ConfirmRazorpayPaymentRequest>
{
    public ConfirmRazorpayPaymentRequestValidator()
    {
        RuleFor(x => x.RazorpayOrderId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RazorpayPaymentId).NotEmpty().MaximumLength(200);
        RuleFor(x => x.RazorpaySignature).NotEmpty().Length(64).Matches("^[0-9a-fA-F]+$");
    }
}
