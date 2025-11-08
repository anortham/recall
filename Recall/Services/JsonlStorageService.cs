using System.Collections.Concurrent;
using System.Text.Json;

namespace Recall;

/// <summary>
/// Service for reading and writing MemoryEvent objects to JSONL files.
/// JSONL format: one JSON object per line, newline-separated.
/// Files are organized by date: .recall/memories/YYYY-MM-DD/memories.jsonl
/// </summary>
public class JsonlStorageService
{
    private readonly string _baseDirectory;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false // JSONL requires single-line JSON
    };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileSemaphores = new();


    public JsonlStorageService(string baseDirectory)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
    }

    /// <summary>
    /// Appends a MemoryEvent to the date-stamped JSONL file and returns its line number.
    /// Creates the directory and file if they don't exist.
    /// This operation is atomic per-file to ensure correct line numbering.
    /// </summary>
    /// <returns>A tuple containing the file path and the 0-indexed line number of the new entry.</returns>
    public async Task<(string FilePath, int LineNumber)> AppendAsync(MemoryEvent memoryEvent)
    {
        ArgumentNullException.ThrowIfNull(memoryEvent);

        // Generate file path based on timestamp: .recall/memories/YYYY-MM-DD/memories.jsonl
        var dateStamp = memoryEvent.Timestamp.ToString("yyyy-MM-dd");
        var directoryPath = Path.Combine(_baseDirectory, "memories", dateStamp);
        var filePath = Path.Combine(directoryPath, "memories.jsonl");

        // Get or create a semaphore for the specific file path to ensure atomic append/line count
        var semaphore = _fileSemaphores.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync();
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(directoryPath);

            // Inefficient for very large files, but necessary for correctness without a more complex system.
            // This is safe because of the semaphore.
            var lineCount = File.Exists(filePath) ? (await File.ReadAllLinesAsync(filePath)).Length : 0;

            // Serialize to single-line JSON
            var json = JsonSerializer.Serialize(memoryEvent, JsonOptions);

            // Append to file (with newline)
            await File.AppendAllTextAsync(filePath, json + Environment.NewLine);

            return (filePath, lineCount);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Reads a specific line from a JSONL file and deserializes it to a MemoryEvent.
    /// Line numbers are 0-based.
    /// </summary>
    /// <returns>The MemoryEvent at the specified line, or null if line doesn't exist.</returns>
    public async Task<MemoryEvent?> ReadLineAsync(string filePath, int lineNumber)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        var lines = await File.ReadAllLinesAsync(filePath);

        if (lineNumber < 0 || lineNumber >= lines.Length)
        {
            return null;
        }

        var json = lines[lineNumber];
        return JsonSerializer.Deserialize<MemoryEvent>(json);
    }

    /// <summary>
    /// Reads all MemoryEvents from a JSONL file.
    /// </summary>
    public async Task<List<MemoryEvent>> ReadAllAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {filePath}", filePath);
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        var events = new List<MemoryEvent>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue; // Skip empty lines
            }

            var memoryEvent = JsonSerializer.Deserialize<MemoryEvent>(line);
            if (memoryEvent != null)
            {
                events.Add(memoryEvent);
            }
        }

        return events;
    }
}
