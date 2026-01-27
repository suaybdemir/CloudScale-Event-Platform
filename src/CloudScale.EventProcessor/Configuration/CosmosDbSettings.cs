namespace CloudScale.EventProcessor.Configuration;

public class CosmosDbSettings
{
    public const string SectionName = "CosmosDb";
    public required string Endpoint { get; set; }
    public required string DatabaseName { get; set; }
    public required string ContainerName { get; set; }
    public int DefaultTtlSeconds { get; set; } = 2592000; // 30 days
}
