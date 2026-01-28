using Azure.Identity;
using Azure.Messaging.ServiceBus;
using CloudScale.EventProcessor.Configuration;
using CloudScale.EventProcessor.Services;
using CloudScale.EventProcessor.Workers;
using Microsoft.Azure.Cosmos;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

// Configuration
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection(CosmosDbSettings.SectionName));

// Services
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["ServiceBus:ConnectionString"];
    var namespaceName = config["ServiceBus:FullyQualifiedNamespace"];
    
    if (string.Equals(namespaceName, "mock", StringComparison.OrdinalIgnoreCase))
    {
        return (ServiceBusClient)null!;
    }

    if (!string.IsNullOrEmpty(connectionString))
    {
        Log.Information("Initializing ServiceBusClient with ConnectionString");
        var options = new ServiceBusClientOptions
        {
            TransportType = ServiceBusTransportType.AmqpTcp
        };
        return new ServiceBusClient(connectionString, options);
    }

    // DefaultCredential or ConnectionString fallback
    return new ServiceBusClient(namespaceName, new DefaultAzureCredential());
});

builder.Services.AddSingleton<CosmosClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["CosmosDb:Endpoint"];
    var accountKey = config["CosmosDb:AccountKey"];
    var env = sp.GetRequiredService<IHostEnvironment>();

    var cosmosClientOptions = new CosmosClientOptions
    {
        ConnectionMode = ConnectionMode.Gateway,
        AllowBulkExecution = true,
        RequestTimeout = TimeSpan.FromMinutes(5),
        MaxRetryAttemptsOnRateLimitedRequests = 10,
        MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(60),
        // LimitToEndpoint = true
    };

    // Development: Bypass SSL for emulator (self-signed cert)
    if (env.IsDevelopment() || (endpoint != null && (endpoint.Contains("cosmosdb-emulator") || endpoint.Contains("localhost:8081"))))
    {
        Log.Warning("Development mode: Bypassing SSL validation for Cosmos DB emulator");
        cosmosClientOptions.HttpClientFactory = () =>
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true,
                    // Allow ANY protocol
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
        Log.Information("Initializing CosmosClient with Account Key (Length: {Length})", accountKey.Length);
        return new CosmosClient(endpoint, accountKey, cosmosClientOptions);
    }
    else 
    {
        Log.Warning("CosmosDb:AccountKey not found in configuration. Fallback to DefaultAzureCredential.");
    }

    // Fallback to Managed Identity
    return new CosmosClient(endpoint, new DefaultAzureCredential(), cosmosClientOptions);
});

builder.Services.AddSingleton<IArchiveService, ArchiveService>();
builder.Services.AddSingleton<IArchiveService, ArchiveService>();

// Cache Registration
var redisConnection = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrEmpty(redisConnection) && redisConnection != "mock")
{
    Log.Information("Using Redis Distributed Cache: {Connection}", redisConnection);
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "CloudScaleProc:";
    });
}
else
{
    Log.Warning("Redis ConnectionString missing. Using InMemory Distributed Cache.");
    builder.Services.AddDistributedMemoryCache();
}

var cosmosEndpoint = builder.Configuration["CosmosDb:Endpoint"];
if (string.Equals(cosmosEndpoint, "mock", StringComparison.OrdinalIgnoreCase))
{
    Log.Warning("Using MOCK CosmosDbService");
    builder.Services.AddSingleton<ICosmosDbService, MockCosmosDbService>();
    // Need to register a dummy CosmosClient or ensure it's not resolved elsewhere?
    // EventProcessorWorker uses ICosmosDbService, so it's fine.
    // ReadModelProjectorWorker?
    // It uses ICosmosDbService? Check constructor.
    // Wait, the original code registered CosmosClient separately.
    // If I don't register CosmosClient, other services relying on it might fail.
    // Let's check dependencies.
}
else
{
    builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();
}
builder.Services.AddSingleton<CloudScale.Shared.Services.IFraudDetectionService, CloudScale.Shared.Services.FraudDetectionService>();
builder.Services.AddSingleton<IUserScoringService, UserScoringService>();
builder.Services.AddSingleton<IBackpressureMonitor, BackpressureMonitor>();
builder.Services.AddHostedService(sp => (BackpressureMonitor)sp.GetRequiredService<IBackpressureMonitor>());
builder.Services.AddHostedService<EventProcessorWorker>();
builder.Services.AddHostedService<ReadModelProjectorWorker>();

var host = builder.Build();
host.Run();
