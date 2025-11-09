using Microsoft.Extensions.DependencyInjection;
using Serilog;

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
    /// Starts watching for file changes.
    /// </summary>
    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        Log.Information("FileWatcher started - monitoring memories/ directory");
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

        // Clear existing index entries for this file to prevent duplicates
        await vectorIndex.DeleteByFileAsync(filePath);

        // Read all events from the file
        var events = await jsonlStorage.ReadAllAsync(filePath);

        Log.Information("Re-indexing {EventCount} memories from {FileName}", events.Count, Path.GetFileName(filePath));

        // Re-index each event
        for (int i = 0; i < events.Count; i++)
        {
            var memoryEvent = events[i];
            var embedding = await embeddingService.GenerateEmbeddingAsync(memoryEvent.Content);

            // Insert with correct line number (0-indexed)
            await vectorIndex.InsertAsync(embedding, memoryEvent.WorkspacePath, filePath, i);
        }

        Log.Information("Successfully re-indexed {EventCount} memories", events.Count);
    }

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _watcher?.Dispose();
    }
}
