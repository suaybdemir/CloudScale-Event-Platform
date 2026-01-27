namespace CloudScale.Shared.Constants;

public static class MessagingConstants
{
    public const string IngestionQueue = "events-ingestion";
    public const string EventsTopic = "events-topic";
    
    public static class MetadataKeys
    {
        public const string ProducerVersion = "ProducerVersion";
        public const string IngestedAt = "IngestedAt";
        public const string ClientIp = "ClientIp";
    }
}
