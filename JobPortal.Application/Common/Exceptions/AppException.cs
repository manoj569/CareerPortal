namespace JobPortal.Application.Common.Exceptions;

public class AppException(string message, int statusCode, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class BadRequestException(string message, string code = "bad_request") : AppException(message, 400, code);
public sealed class UnauthorizedException(string message = "Authentication failed.") : AppException(message, 401, "unauthorized");
public sealed class EmailNotVerifiedException() : AppException("Email verification is required.", 403, "email_not_verified");
public sealed class NotFoundException(string message) : AppException(message, 404, "not_found");
public sealed class ConflictException(string message) : AppException(message, 409, "conflict");
