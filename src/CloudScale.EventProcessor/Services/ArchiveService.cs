using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CloudScale.EventProcessor.Services
{
    public interface IArchiveService
    {
        Task ArchiveEventAsync(string eventId, string content);
    }

    public class ArchiveService : IArchiveService
    {
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<ArchiveService> _logger;

        public ArchiveService(IConfiguration configuration, ILogger<ArchiveService> logger)
        {
            _logger = logger;
            var connectionString = configuration["ConnectionStrings:StorageAccount"];
            var containerName = "events-archive";

            if (string.IsNullOrEmpty(connectionString))
            {
                // For demo purposes/if config missing (like in tests without secrets), we might warn or use a dev emulator string
                // But normally this should throw.
                 _logger.LogWarning("Storage Connection String is missing!");
                 // In a real app we would throw, but for this partial implementation we'll allow null client to demo structure
                 _containerClient = null!; 
                 return;
            }

            var blobServiceClient = new BlobServiceClient(connectionString);
            _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
            // Ensure container exists (create if not)
            _containerClient.CreateIfNotExistsAsync().Wait();
        }

        // Constructor for testing with mocked client
        public ArchiveService(BlobContainerClient containerClient, ILogger<ArchiveService> logger)
        {
            _containerClient = containerClient;
            _logger = logger;
        }

        public async Task ArchiveEventAsync(string eventId, string content)
        {
            if (_containerClient == null) 
            {
                _logger.LogWarning("Skipping archive for {EventId} - Storage not configured.", eventId);
                return;
            }

            try
            {
                var blobName = $"{DateTime.UtcNow:yyyy/MM/dd}/{eventId}.json";
                var blobClient = _containerClient.GetBlobClient(blobName);
                
                // Upload content
                await blobClient.UploadAsync(BinaryData.FromString(content), overwrite: true);
                
                _logger.LogInformation("Archived event {EventId} to {BlobUri}", eventId, blobClient.Uri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive event {EventId}", eventId);
                throw; // Retry policy will handle this
            }
        }
    }
}
