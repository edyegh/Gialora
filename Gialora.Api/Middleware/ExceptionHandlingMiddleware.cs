// Gialora.Api/Middleware/ExceptionHandlingMiddleware.cs
using System.Text.Json;
using Gialora.Application.Common;
using Gialora.Shared.Dtos;

namespace Gialora.Api.Middleware;

/// <summary>
/// Application-layer-ի տիպավորված սխալները վերածում է ճիշտ HTTP status-ի։
///
/// Առանց սրա ամեն controller ստիպված էր try/catch անել և message-ի տեքստով կռահել՝
/// 400 է, 404, թե 409։ Չսպասված սխալները չեն արտահոսում client — վերադարձնում ենք
/// ընդհանուր message, իսկ մանրամասնը գնում է log։
/// </summary>
public class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                // Response-ը արդեն ուղարկվել է — status-ը փոխել այլևս հնարավոր չէ
                _logger.LogError(ex, "Unhandled exception after the response had started");
                throw;
            }

            var (status, payload) = Translate(ex);

            if (status >= StatusCodes.Status500InternalServerError)
                _logger.LogError(ex, "Unhandled exception while processing {Path}", context.Request.Path);
            else
                _logger.LogInformation("Request to {Path} failed: {Message}", context.Request.Path, ex.Message);

            context.Response.Clear();
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }
    }

    private static (int Status, ApiErrorDto Payload) Translate(Exception ex) => ex switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, new ApiErrorDto { Message = ex.Message }),

        ValidationFailedException validation => (
            StatusCodes.Status400BadRequest,
            new ApiErrorDto { Message = validation.Message, Errors = validation.Errors }),

        ConflictException => (StatusCodes.Status409Conflict, new ApiErrorDto { Message = ex.Message }),
        ForbiddenException => (StatusCodes.Status403Forbidden, new ApiErrorDto { Message = ex.Message }),

        // Չսպասված սխալի մանրամասնը երբեք չի հասնում client — stack trace-ը
        // տեղեկություն է տալիս attacker-ին բազայի ու կոդի կառուցվածքի մասին
        _ => (StatusCodes.Status500InternalServerError,
              new ApiErrorDto { Message = "Something went wrong. Please try again." })
    };
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseGialoraExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
