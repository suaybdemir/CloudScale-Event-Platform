using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CloudScale.EventProcessor.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CloudScale.EventProcessor.Tests
{
    public class ArchiveServiceTests
    {
        [Fact]
        public async Task ArchiveEventAsync_ShouldUploadBlob_WhenClientIsConfigured()
        {
            // Arrange
            var mockBlobClient = new Mock<BlobClient>();
            var mockContainerClient = new Mock<BlobContainerClient>();
            var mockLogger = new Mock<ILogger<ArchiveService>>();

            // Setup ContainerClient to return our Mock BlobClient
            mockContainerClient
                .Setup(x => x.GetBlobClient(It.IsAny<string>()))
                .Returns(mockBlobClient.Object);

            // Setup UploadAsync to return a dummy response
            mockBlobClient
                .Setup(x => x.UploadAsync(It.IsAny<BinaryData>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Response<BlobContentInfo>)null!);

            var service = new ArchiveService(mockContainerClient.Object, mockLogger.Object);
            var eventId = "test-event-123";
            var content = "{\"data\": \"value\"}";

            // Act
            await service.ArchiveEventAsync(eventId, content);

            // Assert
            // Verify GetBlobClient was called with correct path structure
            mockContainerClient.Verify(x => x.GetBlobClient(It.Is<string>(s => s.Contains(eventId) && s.Contains(".json"))), Times.Once);

            // Verify UploadAsync was called
            mockBlobClient.Verify(x => x.UploadAsync(It.IsAny<BinaryData>(), true, It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
