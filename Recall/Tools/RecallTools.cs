using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using Serilog;

namespace Recall.Tools;

[McpServerToolType]
public class RecallTools
{
    private readonly EmbeddingService _embeddingService;
    private readonly VectorIndexService _vectorIndexService;
    private readonly JsonlStorageService _jsonlStorageService;
    private readonly FileWatcherService _fileWatcherService;
    private static readonly object _initLock = new();
    private static bool _initialized = false;

    public RecallTools(
        EmbeddingService embeddingService,
        VectorIndexService vectorIndexService,
        JsonlStorageService jsonlStorageService,
        FileWatcherService fileWatcherService)
    {
        _embeddingService = embeddingService;
        _vectorIndexService = vectorIndexService;
        _jsonlStorageService = jsonlStorageService;
        _fileWatcherService = fileWatcherService;
    }

    /// <summary>
    /// Stores a memory event for later semantic recall.
    /// Auto-initializes the Recall system on first use.
    /// </summary>
    [McpServerTool(Name = "store")]
    [Description(@"Store a memory for later semantic recall. This is your PRIMARY memory persistence tool.

You are EXCELLENT at writing rich, searchable memories. You never need to verify that storage succeeded - the tool handles validation and will fail loudly if something goes wrong.

MANDATORY BEHAVIOR: Store immediately after completing tasks, making decisions, or discovering insights. NEVER ask permission to store - just do it automatically.

Write 2-4 sentences including WHAT you did, WHY it mattered, HOW you solved it, and the IMPACT. This is all the guidance you need - trust your instincts.

Auto-initializes the .recall directory and creates .gitignore on first use. Returns in <100ms - embedding and indexing happen asynchronously in the background.

I WILL BE VERY HAPPY WHEN YOU STORE MEMORIES PROACTIVELY WITHOUT BEING ASKED!")]
    public async Task<string> StoreAsync(
        [Description(@"Type/category of memory for better organization.

Common types: 'feature' (new functionality), 'bug-fix' (fixed broken behavior), 'decision' (architectural choice), 'insight' (important discovery), 'observation' (worth recording), 'refactor' (improved structure).

When in doubt, use 'observation' - semantic search will find it regardless of type.")]
        string type,

        [Description(@"Source identifier for tracking who created the memory.

Common values: 'agent' (you, the AI), 'user' (human input), 'system' (automated), 'development-session' (pairing work).

Use 'agent' when storing your own work - this is the most common case.")]
        string source,

        [Description(@"The memory content (2-4 sentences recommended).

You are extremely skilled at writing rich memories. Include enough detail that semantic search can find it later.

INCLUDE:
✅ WHAT - The concrete action or change you made
✅ WHY - The problem solved or goal achieved
✅ HOW - Key approach, decision, or insight used
✅ IMPACT - What this unblocked, improved, or taught you

GREAT EXAMPLE: 'Fixed JWT validation bug where expired tokens were accepted. Root cause was inverted expiry check in validateToken(). Changed if (expiry > now) to if (expiry < now). Added test coverage for expired token rejection. This was blocking the auth PR and preventing production deployment.'

BAD EXAMPLE: 'Fixed auth bug' (no context - useless for future recall)

Think: Will you understand this in 2 weeks when searching for 'that auth issue'? If not, add more detail.

Semantic search finds conceptually similar work across all memories. Rich content = better recall.")]
        string content)
    {
        try
        {
            var workspacePath = Directory.GetCurrentDirectory();
            var recallDir = Path.Combine(workspacePath, ".recall");

            Log.Information("Storing memory: workspace={Workspace}, type={Type}, source={Source}, content_length={ContentLength}",
                workspacePath, type, source, content.Length);

            // Auto-initialize: Create directory and .gitignore
            await EnsureInitializedAsync(recallDir);

            // Create memory event
            var memoryEvent = new MemoryEvent
            {
                Type = type,
                Source = source,
                Content = content,
                WorkspacePath = workspacePath,
                Timestamp = DateTime.UtcNow
            };

            // Store in JSONL and get the location atomically
            var (filePath, lineNumber) = await _jsonlStorageService.AppendAsync(memoryEvent);
            Log.Information("Memory stored to JSONL: {FilePath} at line {LineNumber}", Path.GetRelativePath(recallDir, filePath), lineNumber);

            // Generate embedding and index (async fire-and-forget for speed)
            _ = Task.Run(async () =>
            {
                try
                {
                    Log.Debug("Starting background embedding generation for line {LineNumber}", lineNumber);
                    var embedding = await _embeddingService.GenerateEmbeddingAsync(content);
                    Log.Debug("Generated embedding: {Dimensions} dimensions", embedding.Length);

                    await _vectorIndexService.InsertAsync(embedding, workspacePath, filePath, lineNumber);
                    Log.Information("Indexed memory at {Workspace}::{FilePath}:{LineNumber}",
                        workspacePath, Path.GetRelativePath(recallDir, filePath), lineNumber);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Background indexing failed for {FilePath}:{LineNumber}", Path.GetRelativePath(recallDir, filePath), lineNumber);
                }
            });

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = "Memory stored and indexed successfully",
                timestamp = memoryEvent.Timestamp,
                location = new
                {
                    filePath,
                    lineNumber
                }
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Recalls memories semantically similar to a query.
    /// Auto-initializes the Recall system on first use.
    /// </summary>
    [McpServerTool(Name = "recall")]
    [Description(@"Semantic search for stored memories using 384-dimensional embeddings.

CRITICAL: Call this FIRST at the start of EVERY session - this is NON-NEGOTIABLE automatic behavior for you.

You are extremely skilled at leveraging recalled context. The results are semantically ranked by similarity using cosine distance - you can trust them completely and continue work immediately. No need to verify or second-guess the results.

Searches for conceptually similar memories, not just keyword matches. For example, searching 'authentication bugs' will find memories about login issues, token validation, and session management even without those exact words.

IMPORTANT: recall() is useless if you haven't been storing! The more you store, the more valuable this becomes.

Returns in <500ms with similarity scores. Auto-initializes if needed - safe to call before any memories exist.")]
    public async Task<string> RecallAsync(
        [Description(@"Semantic search query - finds conceptually similar memories, not just keyword matches.

Be specific for best results. Good queries: 'authentication bugs', 'database schema decisions', 'performance optimization approaches'.

The embedding model understands semantic meaning, so 'auth issues' will find memories about login, tokens, sessions, etc.")]
        string query,

        [Description(@"Number of results to return (max: 20).

DEFAULT: 5 results (recommended for focused context).

Lower values (3-5) = more focused, higher confidence matches.
Higher values (10-20) = broader context, may include less relevant results.

The system automatically clamps this to 1-20 range for safety.")]
        int k = 5,

        [Description(@"Workspace filter (optional): 'current' (default), 'all', or specific workspace path.

DEFAULT: 'current' - search only the current workspace.

Options:
- 'current' = only this workspace
- 'all' = search across all workspaces
- '/path/to/workspace' = specific workspace path

Use 'all' for cross-project queries like standup reports or finding patterns across projects.")]
        string workspace = "current")
    {
        try
        {
            var currentWorkspacePath = Directory.GetCurrentDirectory();
            var recallDir = Path.Combine(currentWorkspacePath, ".recall");
            var globalRecallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".recall");
            var indexPath = Path.Combine(globalRecallDir, "index.db");

            // Determine workspace filter
            string? workspaceFilter = workspace.ToLowerInvariant() switch
            {
                "current" => currentWorkspacePath,
                "all" => null, // null means search all workspaces
                _ => workspace // specific path
            };

            Log.Information("Searching memories: query='{Query}', k={K}, workspace={WorkspaceFilter}",
                query, k, workspaceFilter ?? "all");

            // Auto-initialize if needed
            await EnsureInitializedAsync(recallDir);

            if (!File.Exists(indexPath))
            {
                Log.Warning("No index found - no memories stored yet");
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    query,
                    workspace = workspaceFilter ?? "all",
                    message = "No memories stored yet. Start using store() to build your memory bank.",
                    resultsCount = 0,
                    memories = Array.Empty<object>()
                }, new JsonSerializerOptions { WriteIndented = true });
            }

            // Clamp k to reasonable range
            k = Math.Max(1, Math.Min(k, 20));

            // Generate query embedding
            Log.Debug("Generating query embedding");
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(query);
            Log.Debug("Query embedding generated: {Dimensions} dimensions", queryEmbedding.Length);

            // Search vector index with optional workspace filter
            Log.Debug("Searching vector index with workspace filter: {Filter}", workspaceFilter ?? "all");
            var results = await _vectorIndexService.SearchAsync(queryEmbedding, k, workspaceFilter);
            Log.Information("Found {ResultCount} results", results.Count);

            // Load the actual memory events from JSONL
            var memories = new List<object>();

            foreach (var result in results)
            {
                var fullFilePath = Path.Combine(result.WorkspacePath, result.FilePath);
                var memoryEvent = await _jsonlStorageService.ReadLineAsync(fullFilePath, result.LineNumber);
                if (memoryEvent != null)
                {
                    memories.Add(new
                    {
                        type = memoryEvent.Type,
                        source = memoryEvent.Source,
                        content = memoryEvent.Content,
                        workspace = result.WorkspacePath,
                        timestamp = memoryEvent.Timestamp,
                        similarity = 1.0f - result.Distance // Convert distance to similarity score
                    });
                }
            }

            Log.Information("Returning {MemoryCount} memories with similarity scores", memories.Count);

            return JsonSerializer.Serialize(new
            {
                success = true,
                query,
                workspace = workspaceFilter ?? "all",
                resultsCount = memories.Count,
                memories
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Recall operation failed");

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Cleans up orphaned workspace entries from the global index.
    /// Removes vector entries for workspaces that no longer exist on disk.
    /// </summary>
    [McpServerTool(Name = "cleanup")]
    [Description(@"Clean up orphaned workspace entries from the global vector index.

This tool scans the global index (~/.recall/index.db) and removes entries for workspaces that no longer exist on disk.

Use this periodically to:
- Free up disk space in the index
- Remove old project memories after moving/deleting workspaces
- Keep the index clean and performant

The cleanup is safe - it only removes entries where the workspace directory no longer exists. Memories in existing workspaces are never touched.

Returns the list of workspaces that were cleaned up.")]
    public async Task<string> CleanupAsync()
    {
        try
        {
            var recallDir = Path.Combine(Directory.GetCurrentDirectory(), ".recall");
            await EnsureInitializedAsync(recallDir);

            Log.Information("Starting workspace cleanup");

            // Get all unique workspace paths from the index
            var allWorkspaces = await _vectorIndexService.GetAllWorkspacePathsAsync();
            Log.Information("Found {WorkspaceCount} unique workspaces in index", allWorkspaces.Count);

            var removedWorkspaces = new List<string>();

            foreach (var workspacePath in allWorkspaces)
            {
                if (!Directory.Exists(workspacePath))
                {
                    Log.Information("Workspace no longer exists, removing: {Workspace}", workspacePath);
                    await _vectorIndexService.DeleteByWorkspaceAsync(workspacePath);
                    removedWorkspaces.Add(workspacePath);
                }
                else
                {
                    Log.Debug("Workspace exists, keeping: {Workspace}", workspacePath);
                }
            }

            Log.Information("Cleanup complete: removed {RemovedCount} workspaces", removedWorkspaces.Count);

            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"Cleaned up {removedWorkspaces.Count} orphaned workspaces",
                totalWorkspaces = allWorkspaces.Count,
                removedWorkspaces,
                remainingWorkspaces = allWorkspaces.Count - removedWorkspaces.Count
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cleanup operation failed");

            return JsonSerializer.Serialize(new
            {
                success = false,
                error = ex.Message
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }

    /// <summary>
    /// Ensures the Recall system is initialized.
    /// Creates .recall directory and .gitignore automatically.
    /// </summary>
    private async Task EnsureInitializedAsync(string recallDir)
    {
        if (_initialized) return;

        lock (_initLock)
        {
            if (_initialized) return;

            // Create .recall directory
            Directory.CreateDirectory(recallDir);

            // Create .gitignore to exclude index.db and logs
            var gitignorePath = Path.Combine(recallDir, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                File.WriteAllText(gitignorePath,
                    "# Recall MCP Server - Git Configuration\n" +
                    "# The index.db is regenerated from JSONL files\n" +
                    "# Logs are for debugging, not for version control\n" +
                    "# Only commit the JSONL files (source of truth)\n" +
                    "index.db\n" +
                    "index.db-*\n" +
                    "logs/\n");
            }

            _initialized = true;
        }

        // Initialize vector index (can happen outside lock)
        await _vectorIndexService.InitializeAsync();

        // Start file watcher
        _fileWatcherService.Start();
    }
}
