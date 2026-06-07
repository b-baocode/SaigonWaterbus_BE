using SaigonWaterbus.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace SaigonWaterbus.Web.Infrastructure;

/// <summary>
/// Converts well-known application exceptions into RFC 9110-compliant <see cref="ProblemDetails"/> responses,
/// mapping <see cref="ValidationException"/> → 400, <see cref="SaigonWaterbus.Application.Common.Exceptions.NotFoundException"/> → 404,
/// <see cref="UnauthorizedAccessException"/> → 401, and <see cref="ForbiddenAccessException"/> → 403.
/// Unrecognised exceptions are not handled and fall through to the default middleware.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, problemDetails) = exception switch
        {
            AccountNotCompletedException ance => (StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Account not completed",
                Detail = ance.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4",
                Extensions =
                {
                    ["code"] = ance.Code
                }
            }),
            ValidationException ve => (StatusCodes.Status400BadRequest, (ProblemDetails)new ValidationProblemDetails(ve.Errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            SaigonWaterbus.Application.Common.Exceptions.NotFoundException ne => (StatusCodes.Status404NotFound, new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                Title = "The specified resource was not found.",
                Detail = ne.Message
            }),
            UnauthorizedAccessException uae when uae.Message.StartsWith("Google token is invalid:", StringComparison.Ordinal) =>
                (StatusCodes.Status401Unauthorized, new ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = uae.Message,
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
                }),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.2"
            }),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Forbidden",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.4"
            }),
            OtpDispatchException ode => (StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "OTP delivery failed",
                Detail = ode.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4"
            }),
            ProfileImageStorageException pise => (StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Profile image upload failed",
                Detail = pise.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.4"
            }),
            BadHttpRequestException bhre => (StatusCodes.Status400BadRequest, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid request body",
                Detail = bhre.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            JsonException je => (StatusCodes.Status400BadRequest, new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid JSON",
                Detail = je.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
            }),
            _ => (-1, null)
        };

        if (problemDetails is null) return false;

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync((object)problemDetails, cancellationToken);
        return true;
    }
}
