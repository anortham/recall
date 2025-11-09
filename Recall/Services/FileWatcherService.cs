using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Blake3;

namespace Recall;

/// <summary>
/// Watches the .recall/memories/ directory for JSONL file changes
/// and triggers automatic re-indexing.
/// </summary>
public class FileWatcherService : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly string _recallDir;
    private readonly IServiceProvider _serviceProvider;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingFiles = new();
    private readonly object _lock = new();

    public FileWatcherService(string recallDir, IServiceProvider serviceProvider)
    {
        _recallDir = recallDir ?? throw new ArgumentNullException(nameof(recallDir));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        var memoriesDir = Path.Combine(recallDir, "memories");

        // Ensure memories directory exists before watching
        Directory.CreateDirectory(memoriesDir);

        _watcher = new FileSystemWatcher(memoriesDir)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            Filter = "*.jsonl",
            IncludeSubdirectories = true,
            EnableRaisingEvents = false // Start disabled, enable with Start()
        };

        _watcher.Created += OnFileChanged;
        _watcher.Changed += OnFileChanged;

        // Debounce timer: wait 2 seconds after last change before re-indexing
        _debounceTimer = new System.Timers.Timer(2000);
        _debounceTimer.Elapsed += async (sender, e) => await OnDebounceElapsed();
        _debounceTimer.AutoReset = false;

        Log.Debug("FileWatcher initialized for: {MemoriesDir}", memoriesDir);
    }

    /// <summary>
    /// Starts watching for file changes and indexes existing files.
    /// </summary>
    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        Log.Information("FileWatcher started - monitoring memories/ directory");

        // Index existing JSONL files on startup (fire-and-forget)
        _ = Task.Run(async () =>
        {
            try
            {
                var memoriesDir = Path.Combine(_recallDir, "memories");
                if (!Directory.Exists(memoriesDir))
                {
                    return;
                }

                var jsonlFiles = Directory.GetFiles(memoriesDir, "*.jsonl", SearchOption.AllDirectories);
                if (jsonlFiles.Length > 0)
                {
                    Log.Information("Found {FileCount} existing JSONL file(s), starting initial indexing", jsonlFiles.Length);
                    foreach (var filePath in jsonlFiles)
                    {
                        await ReindexFileAsync(filePath);
                    }
                    Log.Information("Initial indexing complete");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to index existing files on startup");
            }
        });
    }

    /// <summary>
    /// Stops watching for file changes.
    /// </summary>
    public void Stop()
    {
        _watcher.EnableRaisingEvents = false;
        _debounceTimer.Stop();
        Log.Information("FileWatcher stopped");
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingFiles.Add(e.FullPath);
            Log.Debug("File change detected: {FilePath} ({ChangeType})", e.FullPath, e.ChangeType);
        }

        // Reset debounce timer
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private async Task OnDebounceElapsed()
    {
        List<string> filesToReindex;
        lock (_lock)
        {
            filesToReindex = new List<string>(_pendingFiles);
            _pendingFiles.Clear();
        }

        if (filesToReindex.Count == 0)
        {
            return;
        }

        Log.Information("Debounce elapsed - re-indexing {FileCount} file(s)", filesToReindex.Count);

        // Re-index each changed file
        foreach (var filePath in filesToReindex)
        {
            try
            {
                await ReindexFileAsync(filePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to re-index {FilePath}", filePath);
            }
        }
    }

    private async Task ReindexFileAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Log.Warning("File no longer exists, skipping: {FilePath}", filePath);
            return;
        }

        Log.Debug("Re-indexing: {FilePath}", filePath);

        // Scope services for this operation
        using var scope = _serviceProvider.CreateScope();
        var jsonlStorage = scope.ServiceProvider.GetRequiredService<JsonlStorageService>();
        var embeddingService = scope.ServiceProvider.GetRequiredService<EmbeddingService>();
        var vectorIndex = scope.ServiceProvider.GetRequiredService<VectorIndexService>();

        await vectorIndex.InitializeAsync();

        // Compute BLAKE3 hash of file content to detect changes
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var hashBytes = Hasher.Hash(fileBytes);
        var currentHash = Convert.ToHexString(hashBytes.AsSpan()).ToLowerInvariant();

        // Read all events from the file
        var events = await jsonlStorage.ReadAllAsync(filePath);

        // Check if file is already indexed with same hash (skip if so, saves GPU cycles)
        var metadata = await vectorIndex.GetFileMetadataAsync(filePath);
        if (metadata != null && metadata.Blake3Hash == currentHash && metadata.IndexedCount == events.Count)
        {
            Log.Debug("File already indexed with matching hash ({Hash}), skipping: {FileName}",
                currentHash[..8], Path.GetFileName(filePath));
            return;
        }

        // Content changed or new file - clear existing index entries to prevent duplicates
        await vectorIndex.DeleteByFileAsync(filePath);

        Log.Information("Re-indexing {EventCount} memories from {FileName}", events.Count, Path.GetFileName(filePath));

        // Infer workspace path from file path for backward compatibility with old JSONL files
        // File path structure: {workspace}/.recall/memories/YYYY-MM-DD/memories.jsonl
        var inferredWorkspace = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(filePath)!, "..", "..", ".."));

        // Generate embeddings in a single GPU batch (much faster than loop)
        var contents = events.Select(e => e.Content).ToArray();
        var embeddings = await embeddingService.GenerateEmbeddingBatchAsync(contents);

        // Insert embeddings sequentially (SQLite doesn't like concurrent writes)
        for (int i = 0; i < events.Count; i++)
        {
            // Use WorkspacePath from memory if available, otherwise use inferred workspace
            var workspacePath = events[i].WorkspacePath ?? inferredWorkspace;

            // Insert with correct line number (0-indexed)
            await vectorIndex.InsertAsync(embeddings[i], workspacePath, filePath, i);
        }

        // Update metadata with new hash and count
        await vectorIndex.SetFileMetadataAsync(filePath, currentHash, events.Count);

        Log.Information("Successfully re-indexed {EventCount} memories (hash: {Hash})", events.Count, currentHash[..8]);
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
    }
}
