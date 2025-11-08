namespace Recall.Tests.Unit;

[TestFixture]
public class EmbeddingServiceTests
{
    [Test]
    public void EmbeddingService_ShouldHaveCorrectEmbeddingDimensions()
    {
        // Arrange & Act - embedding dimension for all-MiniLM-L6-v2 is 384
        var expectedDimension = 384;

        // Assert
        Assert.That(EmbeddingService.EmbeddingDimension, Is.EqualTo(expectedDimension));
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ShouldReturnCorrectDimensions()
    {
        // Arrange
        var service = new EmbeddingService();
        var text = "This is a test sentence for embedding generation.";

        // Act
        var embedding = await service.GenerateEmbeddingAsync(text);

        // Assert
        Assert.That(embedding, Is.Not.Null);
        Assert.That(embedding, Has.Length.EqualTo(384));
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ShouldReturnNormalizedVector()
    {
        // Arrange
        var service = new EmbeddingService();
        var text = "Test sentence";

        // Act
        var embedding = await service.GenerateEmbeddingAsync(text);

        // Assert - embedding vectors should be normalized (magnitude close to 1.0)
        var magnitude = Math.Sqrt(embedding.Sum(x => x * x));
        Assert.That(magnitude, Is.EqualTo(1.0).Within(0.01),
            "Embedding should be normalized to unit length");
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ShouldBeDeterministic()
    {
        // Arrange
        var service = new EmbeddingService();
        var text = "Deterministic test text";

        // Act
        var embedding1 = await service.GenerateEmbeddingAsync(text);
        var embedding2 = await service.GenerateEmbeddingAsync(text);

        // Assert - same input should produce same output
        Assert.That(embedding1, Has.Length.EqualTo(embedding2.Length));
        for (int i = 0; i < embedding1.Length; i++)
        {
            Assert.That(embedding1[i], Is.EqualTo(embedding2[i]).Within(0.0001),
                $"Embeddings should be deterministic at index {i}");
        }
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ShouldHandleEmptyString()
    {
        // Arrange
        var service = new EmbeddingService();

        // Act
        var embedding = await service.GenerateEmbeddingAsync(string.Empty);

        // Assert
        Assert.That(embedding, Is.Not.Null);
        Assert.That(embedding, Has.Length.EqualTo(384));
    }

    [Test]
    public void GenerateEmbeddingAsync_ShouldThrowOnNullInput()
    {
        // Arrange
        var service = new EmbeddingService();

        // Act & Assert
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await service.GenerateEmbeddingAsync(null!));
    }

    [Test]
    public async Task GenerateEmbeddingAsync_ShouldHandleLongText()
    {
        // Arrange
        var service = new EmbeddingService();
        // Model max is 256 tokens - use 150 words which should be within limits after tokenization
        var longText = string.Join(" ", Enumerable.Repeat("word", 150));

        // Act
        var embedding = await service.GenerateEmbeddingAsync(longText);

        // Assert - should still return 384 dimensions even with long input
        Assert.That(embedding, Has.Length.EqualTo(384));
    }

    [Test]
    public async Task Dispose_ShouldCleanUpResources()
    {
        // Arrange
        var service = new EmbeddingService();
        await service.GenerateEmbeddingAsync("test");

        // Act
        service.Dispose();

        // Assert - calling dispose multiple times should be safe
        Assert.DoesNotThrow(() => service.Dispose());
    }
}
