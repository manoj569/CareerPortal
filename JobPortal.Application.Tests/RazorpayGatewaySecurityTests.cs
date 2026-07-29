using System.Security.Cryptography;
using System.Text;
using JobPortal.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class RazorpayGatewaySecurityTests
{
    [Fact]
    public void CheckoutAndWebhookSignaturesUseTheirSeparateSecrets()
    {
        const string keySecret = "test_key_secret";
        const string webhookSecret = "test_webhook_secret";
        var gateway = CreateGateway("rzp_test_example", keySecret, webhookSecret);
        var checkoutPayload = "order_1|pay_1";
        var checkoutSignature = Sign(Encoding.UTF8.GetBytes(checkoutPayload), keySecret);
        var webhookPayload = Encoding.UTF8.GetBytes("""{"event":"payment.captured"}""");
        var webhookSignature = Sign(webhookPayload, webhookSecret);

        Assert.True(gateway.VerifyPaymentSignature("order_1", "pay_1", checkoutSignature));
        Assert.False(gateway.VerifyPaymentSignature("order_1", "pay_other", checkoutSignature));
        Assert.True(gateway.VerifyWebhookSignature(webhookPayload, webhookSignature));
        Assert.False(gateway.VerifyWebhookSignature(webhookPayload, checkoutSignature));
    }

    [Fact]
    public void LiveModeKeyIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CreateGateway("rzp_live_forbidden", "secret", "webhook"));
    }

    private static RazorpayGateway CreateGateway(
        string keyId, string keySecret, string webhookSecret)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Razorpay:KeyId"] = keyId,
                ["Razorpay:KeySecret"] = keySecret,
                ["Razorpay:WebhookSecret"] = webhookSecret
            }).Build();
        return new(configuration);
    }

    private static string Sign(byte[] payload, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), payload))
            .ToLowerInvariant();
}
