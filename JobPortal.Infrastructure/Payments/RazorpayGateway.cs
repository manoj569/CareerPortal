using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JobPortal.Application.Abstractions.Payments;
using Microsoft.Extensions.Configuration;

namespace JobPortal.Infrastructure.Payments;

public sealed class RazorpayGateway : IRazorpayGateway
{
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("https://api.razorpay.com/v1/"),
        Timeout = TimeSpan.FromSeconds(15)
    };
    private readonly string _keySecret;

    public RazorpayGateway(IConfiguration configuration)
    {
        KeyId = configuration["Razorpay:KeyId"]
            ?? throw new InvalidOperationException("Razorpay KeyId is not configured.");
        _keySecret = configuration["Razorpay:KeySecret"]
            ?? throw new InvalidOperationException("Razorpay KeySecret is not configured.");
    }

    public string KeyId { get; }

    public async Task<RazorpayOrder> CreateOrderAsync(
        long amountInMinorUnits, string currencyCode, string receipt,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "orders");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{KeyId}:{_keySecret}")));
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            amount = amountInMinorUnits,
            currency = currencyCode,
            receipt
        }), Encoding.UTF8, "application/json");

        using var response = await Client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        return new RazorpayOrder(
            root.GetProperty("id").GetString()!,
            root.GetProperty("amount").GetInt64(),
            root.GetProperty("currency").GetString()!,
            root.GetProperty("receipt").GetString()!);
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(_keySecret),
            Encoding.UTF8.GetBytes($"{orderId}|{paymentId}"));
        return TryDecodeHex(signature, out var supplied) &&
               CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        try { bytes = Convert.FromHexString(value); return true; }
        catch (FormatException) { bytes = []; return false; }
    }
}

public sealed class ConfigurationMembershipPlanProvider(IConfiguration configuration) : IMembershipPlanProvider
{
    public MembershipPlan GetDefaultPlan()
    {
        var section = configuration.GetSection("Membership:DefaultPlan");
        var name = section["Name"] ?? "Job Application Access";
        var currency = section["CurrencyCode"] ?? "INR";
        if (!decimal.TryParse(section["Amount"], out var amount) || amount <= 0)
            throw new InvalidOperationException("Membership plan amount must be configured and greater than zero.");
        if (!int.TryParse(section["DurationDays"], out var duration) || duration <= 0)
            throw new InvalidOperationException("Membership plan duration must be configured and greater than zero.");
        return new MembershipPlan(name, amount, currency, duration);
    }
}
