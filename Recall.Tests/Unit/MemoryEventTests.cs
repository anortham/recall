using System.Text.Json;

namespace Recall.Tests.Unit;

[TestFixture]
public class MemoryEventTests
{
    [Test]
    public void MemoryEvent_ShouldSerializeToJson()
    {
        // Arrange
        var memoryEvent = new MemoryEvent
        {
            Type = "chat_message",
            Source = "user_a",
            Content = "The user seems frustrated about the slow API response time.",
            Timestamp = DateTime.Parse("2025-11-08T10:30:00Z").ToUniversalTime()
        };

        // Act
        string json = JsonSerializer.Serialize(memoryEvent);

        // Assert
        Assert.That(json, Does.Contain("\"type\":\"chat_message\""));
        Assert.That(json, Does.Contain("\"source\":\"user_a\""));
        Assert.That(json, Does.Contain("\"content\":\"The user seems frustrated"));
    }

    [Test]
    public void MemoryEvent_ShouldDeserializeFromJson()
    {
        // Arrange
        string json = """
        {
          "type": "chat_message",
          "source": "user_a",
          "content": "The user seems frustrated about the slow API response time.",
          "timestamp": "2025-11-08T10:30:00Z"
        }
        """;

        // Act
        var memoryEvent = JsonSerializer.Deserialize<MemoryEvent>(json);

        // Assert
        Assert.That(memoryEvent, Is.Not.Null);
        Assert.That(memoryEvent!.Type, Is.EqualTo("chat_message"));
        Assert.That(memoryEvent.Source, Is.EqualTo("user_a"));
        Assert.That(memoryEvent.Content, Does.Contain("frustrated"));
        Assert.That(memoryEvent.Timestamp.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    [Test]
    public void MemoryEvent_ShouldSerializeToSingleLineJson()
    {
        // Arrange
        var memoryEvent = new MemoryEvent
        {
            Type = "chat_message",
            Source = "test",
            Content = "test content",
            Timestamp = DateTime.UtcNow
        };

        // Act - JSONL requires single line, no pretty printing
        string json = JsonSerializer.Serialize(memoryEvent, new JsonSerializerOptions
        {
            WriteIndented = false
        });

        // Assert - should be single line (no newlines in the JSON itself)
        Assert.That(json, Does.Not.Contain("\n"));
        Assert.That(json, Does.Not.Contain("\r"));
    }

    [Test]
    public void MemoryEvent_ShouldRoundTripPerfectly()
    {
        // Arrange
        var original = new MemoryEvent
        {
            Type = "decision_point",
            Source = "agent_alpha",
            Content = "User chose option B over A due to cost concerns.",
            Timestamp = new DateTime(2025, 11, 8, 15, 30, 45, DateTimeKind.Utc)
        };

        // Act
        string json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<MemoryEvent>(json);

        // Assert
        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Type, Is.EqualTo(original.Type));
        Assert.That(deserialized.Source, Is.EqualTo(original.Source));
        Assert.That(deserialized.Content, Is.EqualTo(original.Content));
        Assert.That(deserialized.Timestamp, Is.EqualTo(original.Timestamp));
    }
}
