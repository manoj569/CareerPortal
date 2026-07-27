using FluentValidation;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Shared.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.API.Middleware;

public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger)
{
    private static readonly Action<ILogger, string, Exception?> RequestCancelled =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(3001, nameof(RequestCancelled)),
            "Request cancelled by client for {RequestPath}");
    private static readonly Action<ILogger, string, string, Exception?> UnhandledRequestException =
        LoggerMessage.Define<string, string>(LogLevel.Error, new EventId(3002, nameof(UnhandledRequestException)),
            "Unhandled exception for {RequestMethod} {RequestPath}");
    private static readonly Action<ILogger, int, string, string, string, string, Exception?> ExpectedRequestFailure =
        LoggerMessage.Define<int, string, string, string, string>(LogLevel.Warning, new EventId(3003, nameof(ExpectedRequestFailure)),
            "Request failed with {StatusCode} {ErrorCode} for {RequestMethod} {RequestPath}: {ErrorMessage}");

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                RequestCancelled(logger, context.Request.Path, null);
                return;
            }

            var (statusCode, error) = exception switch
            {
                AppException appException => (appException.StatusCode, new ApiError(appException.Code, appException.Message)),
                ValidationException validationException => (StatusCodes.Status400BadRequest, new ApiError("validation_error", "One or more validation errors occurred.", validationException.Errors
                    .GroupBy(x => x.PropertyName)
                    .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).ToArray()))),
                BadHttpRequestException => (StatusCodes.Status400BadRequest, new ApiError("invalid_request", "The request is invalid.")),
                DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, new ApiError("concurrency_conflict", "The resource was modified by another request.")),
                DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                    (StatusCodes.Status409Conflict, new ApiError("data_conflict", "A resource with the same unique value already exists.")),
                _ => (StatusCodes.Status500InternalServerError, ApiError.InternalServerError())
            };

            if (statusCode >= 500)
                UnhandledRequestException(logger, context.Request.Method, context.Request.Path, exception);
            else
                ExpectedRequestFailure(logger, statusCode, error.Code, context.Request.Method,
                    context.Request.Path, exception.Message, null);

            if (context.Response.HasStarted) throw;
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(error, context.RequestAborted);
        }
    }
}
