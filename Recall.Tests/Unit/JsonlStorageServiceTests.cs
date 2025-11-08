using System.Text.Json;

namespace Recall.Tests.Unit;

[TestFixture]
public class JsonlStorageServiceTests
{
    private string _testDirectory = null!;
    private JsonlStorageService _service = null!;

    [SetUp]
    public void Setup()
    {
        // Create a temporary test directory for each test
        _testDirectory = Path.Combine(Path.GetTempPath(), $"recall_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _service = new JsonlStorageService(_testDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Test]
    public async Task AppendAsync_ShouldCreateDateStampedFile()
    {
        // Arrange
        var memoryEvent = new MemoryEvent
        {
            Type = "test",
            Source = "test",
            Content = "test content",
            Timestamp = new DateTime(2025, 11, 8, 10, 30, 0, DateTimeKind.Utc)
        };

        // Act
        await _service.AppendAsync(memoryEvent);

        // Assert - file should be created at .recall/memories/2025-11-08/memories.jsonl
        var expectedPath = Path.Combine(_testDirectory, "memories", "2025-11-08", "memories.jsonl");
        Assert.That(File.Exists(expectedPath), Is.True, $"Expected file at {expectedPath}");
    }

    [Test]
    public async Task AppendAsync_ShouldWriteSingleLineJson()
    {
        // Arrange
        var memoryEvent = new MemoryEvent
        {
            Type = "chat_message",
            Source = "user_a",
            Content = "Test message",
            Timestamp = DateTime.UtcNow
        };

        // Act
        await _service.AppendAsync(memoryEvent);

        // Assert
        var filePath = Path.Combine(_testDirectory, "memories", DateTime.UtcNow.ToString("yyyy-MM-dd"), "memories.jsonl");
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.That(lines, Has.Length.EqualTo(1));
        Assert.That(lines[0], Does.Contain("\"type\":\"chat_message\""));
        Assert.That(lines[0], Does.Not.Contain("\n")); // Single line
    }

    [Test]
    public async Task AppendAsync_ShouldAppendMultipleEvents()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var event1 = new MemoryEvent
        {
            Type = "event1",
            Source = "source1",
            Content = "Content 1",
            Timestamp = timestamp
        };
        var event2 = new MemoryEvent
        {
            Type = "event2",
            Source = "source2",
            Content = "Content 2",
            Timestamp = timestamp
        };

        // Act
        await _service.AppendAsync(event1);
        await _service.AppendAsync(event2);

        // Assert
        var filePath = Path.Combine(_testDirectory, "memories", timestamp.ToString("yyyy-MM-dd"), "memories.jsonl");
        var lines = await File.ReadAllLinesAsync(filePath);

        Assert.That(lines, Has.Length.EqualTo(2));
        Assert.That(lines[0], Does.Contain("\"type\":\"event1\""));
        Assert.That(lines[1], Does.Contain("\"type\":\"event2\""));
    }

    [Test]
    public async Task ReadLineAsync_ShouldReturnMemoryEventAtSpecificLine()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var event1 = new MemoryEvent
        {
            Type = "first",
            Source = "source",
            Content = "First event",
            Timestamp = timestamp
        };
        var event2 = new MemoryEvent
        {
            Type = "second",
            Source = "source",
            Content = "Second event",
            Timestamp = timestamp
        };

        await _service.AppendAsync(event1);
        await _service.AppendAsync(event2);

        var filePath = Path.Combine(_testDirectory, "memories", timestamp.ToString("yyyy-MM-dd"), "memories.jsonl");

        // Act
        var result = await _service.ReadLineAsync(filePath, lineNumber: 1); // 1-based line number

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Type, Is.EqualTo("second"));
        Assert.That(result.Content, Is.EqualTo("Second event"));
    }

    [Test]
    public async Task ReadAllAsync_ShouldReturnAllEventsFromFile()
    {
        // Arrange
        var timestamp = new DateTime(2025, 11, 8, 12, 0, 0, DateTimeKind.Utc);
        var events = new[]
        {
            new MemoryEvent { Type = "a", Source = "s", Content = "Content A", Timestamp = timestamp },
            new MemoryEvent { Type = "b", Source = "s", Content = "Content B", Timestamp = timestamp },
            new MemoryEvent { Type = "c", Source = "s", Content = "Content C", Timestamp = timestamp }
        };

        foreach (var evt in events)
        {
            await _service.AppendAsync(evt);
        }

        var filePath = Path.Combine(_testDirectory, "memories", "2025-11-08", "memories.jsonl");

        // Act
        var results = await _service.ReadAllAsync(filePath);

        // Assert
        Assert.That(results, Has.Count.EqualTo(3));
        Assert.That(results[0].Type, Is.EqualTo("a"));
        Assert.That(results[1].Type, Is.EqualTo("b"));
        Assert.That(results[2].Type, Is.EqualTo("c"));
    }

    [Test]
    public async Task ReadLineAsync_ShouldReturnNullForInvalidLineNumber()
    {
        // Arrange
        var timestamp = DateTime.UtcNow;
        var memoryEvent = new MemoryEvent
        {
            Type = "test",
            Source = "test",
            Content = "test",
            Timestamp = timestamp
        };
        await _service.AppendAsync(memoryEvent);

        var filePath = Path.Combine(_testDirectory, "memories", timestamp.ToString("yyyy-MM-dd"), "memories.jsonl");

        // Act
        var result = await _service.ReadLineAsync(filePath, lineNumber: 99); // Line doesn't exist

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ReadLineAsync_ShouldThrowIfFileDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "does-not-exist.jsonl");

        // Act & Assert
        Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await _service.ReadLineAsync(nonExistentPath, lineNumber: 0));
    }
}
