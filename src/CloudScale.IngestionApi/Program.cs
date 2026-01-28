using Azure.Messaging.ServiceBus;
using CloudScale.IngestionApi.Endpoints;
using CloudScale.IngestionApi.Middleware;
using CloudScale.IngestionApi.Services;
using CloudScale.IngestionApi.Telemetry;
using CloudScale.Shared.Validation;
using FluentValidation;
using Serilog;
using Azure.Identity;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using Azure.Monitor.OpenTelemetry.Exporter;

var builder = WebApplication.CreateBuilder(args);

// 1. Logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxConcurrentConnections = 10000;
    options.Limits.MaxConcurrentUpgradedConnections = 10000;
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

// 2. Services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 2a. OpenTelemetry - Distributed Tracing & Metrics
var otelResourceBuilder = ResourceBuilder.CreateDefault()
    .AddService("CloudScale.IngestionApi", serviceVersion: "1.0.0");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => 
    {
        tracing
            .SetResourceBuilder(otelResourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(EventMetrics.ActivitySource.Name);
        
        // Azure Monitor exporter (if connection string available)
        var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            tracing.AddAzureMonitorTraceExporter(o => o.ConnectionString = appInsightsConnectionString);
        }
        
        // OTLP exporter for local dev (Jaeger, etc.)
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrEmpty(otlpEndpoint))
        {
            tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(otelResourceBuilder)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddMeter(EventMetrics.MeterName);
        
        var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
        if (!string.IsNullOrEmpty(appInsightsConnectionString))
        {
            metrics.AddAzureMonitorMetricExporter(o => o.ConnectionString = appInsightsConnectionString);
        }
    });

// Core Services
builder.Services.AddCors();
builder.Services.AddScoped<IEventEnrichmentService, EventEnrichmentService>();
builder.Services.AddScoped<IEventEnrichmentService, EventEnrichmentService>();

// Cache Registration (Redis or Memory)
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConnection) && redisConnection != "mock")
{
    Log.Information("Using Redis Distributed Cache: {Connection}", redisConnection);
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "CloudScaleInst:";
    });
}
else
{
    Log.Warning("Redis ConnectionString missing or mock. Using InMemory Distributed Cache.");
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton<CloudScale.Shared.Services.IFraudDetectionService, CloudScale.Shared.Services.FraudDetectionService>();
builder.Services.AddSingleton<IStatsService, InMemoryStatsService>();
builder.Services.AddHttpClient(); 
builder.Services.AddHostedService<CanaryWatchdogService>();
builder.Services.AddSingleton<SystemHealthWatcher>();
builder.Services.AddSingleton<ISystemHealthProvider>(sp => sp.GetRequiredService<SystemHealthWatcher>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<SystemHealthWatcher>());
// builder.Services.AddScoped<IServiceBusProducer, ServiceBusProducerService>(); // Removed duplicate, handled below based on config

// Validation
builder.Services.AddValidatorsFromAssemblyContaining<PageViewEventValidator>();

// 2b. Cosmos DB Client
// 2b. Cosmos DB Client
builder.Services.AddSingleton<Microsoft.Azure.Cosmos.CosmosClient>(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["CosmosDb:Endpoint"];
    var env = sp.GetRequiredService<IHostEnvironment>();
    
    // Check for mocking or fallback
    if (string.IsNullOrEmpty(endpoint) || string.Equals(endpoint, "mock", StringComparison.OrdinalIgnoreCase)) 
    {
        Log.Warning("CosmosDb:Endpoint is missing or 'mock'. CosmosClient will be null.");
        return null!; // Dangerous if injected.
    }
    
    Log.Information("Initializing CosmosClient with Endpoint: {Endpoint}", endpoint);
    var accountKey = config["CosmosDb:AccountKey"];
    
    var cosmosClientOptions = new Microsoft.Azure.Cosmos.CosmosClientOptions
    {
        ConnectionMode = Microsoft.Azure.Cosmos.ConnectionMode.Gateway,
        RequestTimeout = TimeSpan.FromMinutes(5),
        MaxRetryAttemptsOnRateLimitedRequests = 10,
        MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(60),
    };

    // Development: Bypass SSL for emulator (self-signed cert)
    if (env.IsDevelopment() || endpoint.Contains("cosmosdb-emulator") || endpoint.Contains("localhost:8081"))
    {
        Log.Warning("Development mode: Bypassing SSL validation for Cosmos DB emulator");
        cosmosClientOptions.HttpClientFactory = () =>
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.None 
                },
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                ConnectTimeout = TimeSpan.FromMinutes(2)
            };
            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        };
    }

    if (!string.IsNullOrEmpty(accountKey))
    {
        Log.Information("Initializing CosmosClient with Account Key authentication");
        return new Microsoft.Azure.Cosmos.CosmosClient(endpoint, accountKey, cosmosClientOptions);
    }

    Log.Information("Initializing CosmosClient with DefaultAzureCredential");
    return new Microsoft.Azure.Cosmos.CosmosClient(endpoint, new DefaultAzureCredential(), cosmosClientOptions);
});

// Azure Client Registration
builder.Services.AddSingleton(sp => 
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["ServiceBus:ConnectionString"];
    var namespaceName = config["ServiceBus:FullyQualifiedNamespace"];
    
    // Check for Mock Mode
    if (string.Equals(namespaceName, "mock", StringComparison.OrdinalIgnoreCase))
    {
        return (ServiceBusClient)null!;
    }
    
    // Use Connection String (Local Emulator / Shared Key)
    if (!string.IsNullOrEmpty(connectionString))
    {
         Log.Information("Initializing ServiceBusClient with ConnectionString");
         var options = new ServiceBusClientOptions
         {
             TransportType = ServiceBusTransportType.AmqpTcp
         };
         return new ServiceBusClient(connectionString, options);
    }
    
    // Use Managed Identity (Production / Azure)
    Log.Information("Initializing ServiceBusClient with Managed Identity ({Namespace})", namespaceName);
    return new ServiceBusClient(namespaceName, new DefaultAzureCredential());
});



// Conditional Producer Injection
var sbNamespace = builder.Configuration["ServiceBus:FullyQualifiedNamespace"];
if (string.Equals(sbNamespace, "mock", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddScoped<IServiceBusProducer, CloudScale.IngestionApi.Services.Mocks.MockServiceBusProducer>();
    Log.Warning("Using MOCK Service Bus Producer");
}
else
{
    builder.Services.AddSingleton<IServiceBusProducer, ServiceBusProducerService>();
}

var app = builder.Build();

// 3. Pipeline
app.UseRateLimiting(); // Rate limiting first - reject early before processing
app.UseApiKey();         // Followed by Auth
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseCors(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()); // Enable CORS for Dashboard
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Map Endpoints
app.MapEventEndpoints();
app.MapReadEndpoints();

// Health Check
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

try
{
    Log.Information("Starting Ingestion API");
    
    // Auto-Initialize Cosmos DB in Background (Non-blocking)
    using (var scope = app.Services.CreateScope())
    {
        var client = scope.ServiceProvider.GetService<Microsoft.Azure.Cosmos.CosmosClient>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        
            if (client != null)
            {
                var dbName = config["CosmosDb:DatabaseName"] ?? "EventsDb";
                var containerName = config["CosmosDb:ContainerName"] ?? "Events";

                _ = Task.Run(async () => 
                {
                    try 
                    {
                        var db = await client.CreateDatabaseIfNotExistsAsync(dbName);
                        await db.Database.CreateContainerIfNotExistsAsync(containerName, "/PartitionKey");
                        Log.Information("Cosmos DB {DbName} and Container {ContainerName} initialized successfully.", dbName, containerName);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to initialize Cosmos DB at startup.");
                    }
                });
            }
    }

    app.Run();

}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }
