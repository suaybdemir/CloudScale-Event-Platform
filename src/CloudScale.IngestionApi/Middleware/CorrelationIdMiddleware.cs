using Serilog.Context;

namespace CloudScale.IngestionApi.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string CorrelationIdHeader = "X-Correlation-Id";

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();

        if (string.IsNullOrEmpty(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
            context.Request.Headers.Append(CorrelationIdHeader, correlationId);
        }

        context.TraceIdentifier = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.Append(CorrelationIdHeader, correlationId);
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
