using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace CloudScale.IngestionApi.Middleware;

/// <summary>
/// Rate limiting middleware using Token Bucket (per-IP) + Sliding Window (global).
/// Returns 429 Too Many Requests with Retry-After header when limits exceeded.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    
    // Global sliding window limiter
    private readonly SlidingWindowRateLimiter _globalLimiter;
    
    // Per-IP token bucket limiters
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _ipLimiters = new();
    
    // Configuration constants
    private const int GlobalWindowSeconds = 60;
    private readonly IConfiguration _config;

    public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _config = config;
        
        // Read from config or default (Global: 10k/min)
        int globalLimit = _config.GetValue<int>("RateLimiting:GlobalPermitLimit", 10000);
        
        _globalLimiter = new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
        {
            PermitLimit = globalLimit,
            Window = TimeSpan.FromSeconds(GlobalWindowSeconds),
            SegmentsPerWindow = 6, 
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip rate limiting for health checks
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var clientIp = GetClientIp(context);
        
        // 1. Check global limit first
        using var globalLease = await _globalLimiter.AcquireAsync();
        if (!globalLease.IsAcquired)
        {
            _logger.LogWarning("Global rate limit exceeded. Rejecting request.");
            await WriteRateLimitResponse(context, "Global rate limit exceeded", 60);
            return;
        }

        // 2. Check per-IP limit
        var ipLimiter = _ipLimiters.GetOrAdd(clientIp, _ => CreateIpLimiter());
        using var ipLease = await ipLimiter.AcquireAsync();
        
        if (!ipLease.IsAcquired)
        {
            _logger.LogWarning("IP rate limit exceeded for {ClientIp}", clientIp);
            await WriteRateLimitResponse(context, $"Rate limit exceeded for IP: {clientIp}", 10);
            return;
        }

        await _next(context);
    }

    private TokenBucketRateLimiter CreateIpLimiter()
    {
        // Read from config (TokensPerSecond from appsettings matches our intent)
        // RateLimiting:BurstCapacity -> TokenLimit
        // RateLimiting:TokensPerSecond -> TokensPerPeriod
        int burstCapacity = _config.GetValue<int>("RateLimiting:BurstCapacity", 100);
        int tokensPerSecond = _config.GetValue<int>("RateLimiting:TokensPerSecond", 10);

        return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = burstCapacity,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = tokensPerSecond,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private static string GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For header (for proxies/load balancers)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            var ip = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(ip))
                return ip;
        }

        // Fallback to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static async Task WriteRateLimitResponse(HttpContext context, string message, int retryAfterSeconds)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
        context.Response.ContentType = "application/json";
        
        var response = new
        {
            error = "TooManyRequests",
            message,
            retryAfterSeconds
        };
        
        await context.Response.WriteAsJsonAsync(response);
    }
}

/// <summary>
/// Extension method for cleaner registration
/// </summary>
public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseRateLimiting(this IApplicationBuilder app)
    {
        return app.UseMiddleware<RateLimitingMiddleware>();
    }
}
