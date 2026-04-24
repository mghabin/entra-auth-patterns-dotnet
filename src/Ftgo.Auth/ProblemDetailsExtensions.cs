using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ftgo.Auth;

/// <summary>
/// Wires ASP.NET Core's ProblemDetails pipeline + a global exception handler that emits
/// RFC 7807 responses and never leaks stack traces to callers.
/// </summary>
public static class ProblemDetailsExtensions
{
    public static IServiceCollection AddEntraAuthProblemDetails(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
        {
            ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<EntraAuthExceptionHandler>();

        return services;
    }

    public static IApplicationBuilder UseEntraAuthProblemDetails(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }
}

internal sealed partial class EntraAuthExceptionHandler(ILogger<EntraAuthExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogUnhandled(logger, exception, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        var pds = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        return await pds.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Type   = "https://httpstatuses.com/500",
                Title  = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = null, // never leak exception message
            },
        });
    }

    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Path}")]
    private static partial void LogUnhandled(ILogger logger, Exception ex, string path);
}
