using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace JobPortal.API.Swagger;

public sealed class AuthExamplesOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var requestBody = operation.RequestBody;
        if (requestBody is null || !requestBody.Content.TryGetValue("application/json", out var mediaType)) return;

        mediaType.Example = context.MethodInfo.Name switch
        {
            "Register" => Object(("email", "candidate@example.com"), ("password", "Str0ng!Password2026"), ("firstName", "Avery"), ("lastName", "Patel"), ("phoneNumber", "+15551234567")),
            "Login" => Object(("email", "candidate@example.com"), ("password", "Str0ng!Password2026")),
            "VerifyEmail" => Object(("email", "candidate@example.com"), ("token", "verification-token-from-email")),
            "ResendVerification" => Object(("email", "candidate@example.com")),
            "Refresh" or "Logout" => Object(("refreshToken", "Base64 refresh token")),
            "ForgotPassword" => Object(("email", "candidate@example.com")),
            "ResetPassword" => Object(("email", "candidate@example.com"), ("token", "reset-token-from-email"), ("newPassword", "N3w!StrongerPassword")),
            "ChangePassword" => Object(("currentPassword", "Str0ng!Password2026"), ("newPassword", "N3w!StrongerPassword")),
            "CreateOrder" => new OpenApiObject(),
            "Confirm" => Object(
                ("razorpayOrderId", "order_test_example"),
                ("razorpayPaymentId", "pay_test_example"),
                ("razorpaySignature", new string('a', 64))),
            _ => null
        };
    }

    private static OpenApiObject Object(params (string Key, string Value)[] values)
    {
        var example = new OpenApiObject();
        foreach (var (key, value) in values) example[key] = new OpenApiString(value);
        return example;
    }
}
