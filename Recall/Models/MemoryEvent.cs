using System.Text.Json.Serialization;

namespace Recall;

/// <summary>
/// Represents a memory event that can be stored and recalled by the MCP server.
/// </summary>
public class MemoryEvent
{
    /// <summary>
    /// The type/category of the memory (e.g., "chat_message", "decision_point").
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    /// <summary>
    /// The source of the memory (e.g., "user_a", "agent_alpha").
    /// </summary>
    [JsonPropertyName("source")]
    public required string Source { get; set; }

    /// <summary>
    /// The actual content/text of the memory.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; set; }

    /// <summary>
    /// The workspace path where this memory originated.
    /// Optional for backward compatibility with pre-multi-workspace JSONL files.
    /// </summary>
    [JsonPropertyName("workspace_path")]
    public string? WorkspacePath { get; set; }

    /// <summary>
    /// When this memory was created (UTC).
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }
}
