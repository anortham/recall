using Microsoft.Data.Sqlite;
using System.Text;

namespace Recall;

/// <summary>
/// Service for managing the vector search index using sqlite-vec.
/// Stores embeddings with references to JSONL file paths and line numbers.
/// </summary>
public class VectorIndexService : IDisposable
{
    private readonly string _databasePath;
    private SqliteConnection? _connection;
    private bool _initialized = false;
    private bool _disposed = false;

    public VectorIndexService(string databasePath)
    {
        _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
    }

    /// <summary>
    /// Initializes the database and creates the vec0 virtual table.
    /// Loads the vec0 extension for fast vector search.
    /// </summary>
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        await _connection.OpenAsync();

        // Load vec0 extension
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var extensionPath = Path.Combine(baseDir, "Assets", "sqlite-vec", "vec0");

        _connection.EnableExtensions(true);
        _connection.LoadExtension(extensionPath);

        // Create virtual table using vec0 for vector search
        // Note: vec0 virtual tables handle storage differently than regular tables
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            CREATE VIRTUAL TABLE IF NOT EXISTS memories USING vec0(
                embedding float[384],
                +file_path TEXT,
                +line_number INTEGER
            );
        ";
        await command.ExecuteNonQueryAsync();

        _initialized = true;
    }

    /// <summary>
    /// Inserts an embedding vector with its source reference.
    /// </summary>
    public async Task InsertAsync(float[] embedding, string filePath, int lineNumber)
    {
        if (!_initialized || _connection == null)
        {
            throw new InvalidOperationException("VectorIndexService must be initialized before use.");
        }

        ArgumentNullException.ThrowIfNull(embedding);
        ArgumentNullException.ThrowIfNull(filePath);

        if (embedding.Length != 384)
        {
            throw new ArgumentException("Embedding must be 384 dimensions", nameof(embedding));
        }

        // Convert float[] to JSON array format that vec0 expects
        var embeddingJson = "[" + string.Join(", ", embedding.Select(f => f.ToString("G9"))) + "]";

        using var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO memories (embedding, file_path, line_number)
            VALUES ($embedding, $filePath, $lineNumber);
        ";
        command.Parameters.AddWithValue("$embedding", embeddingJson);
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$lineNumber", lineNumber);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Searches for the k nearest neighbors to the query embedding.
    /// Uses vec0 extension for fast KNN search.
    /// </summary>
    public async Task<List<VectorSearchResult>> SearchAsync(float[] queryEmbedding, int k)
    {
        if (!_initialized || _connection == null)
        {
            throw new InvalidOperationException("VectorIndexService must be initialized before use.");
        }

        ArgumentNullException.ThrowIfNull(queryEmbedding);

        if (queryEmbedding.Length != 384)
        {
            throw new ArgumentException("Query embedding must be 384 dimensions", nameof(queryEmbedding));
        }

        // Convert query embedding to JSON array format that vec0 expects
        var queryJson = "[" + string.Join(", ", queryEmbedding.Select(f => f.ToString("G9"))) + "]";

        var results = new List<VectorSearchResult>();

        using var command = _connection.CreateCommand();
        command.CommandText = @"
            SELECT file_path, line_number, distance
            FROM memories
            WHERE embedding MATCH $query
            ORDER BY distance
            LIMIT $k;
        ";
        command.Parameters.AddWithValue("$query", queryJson);
        command.Parameters.AddWithValue("$k", k);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new VectorSearchResult
            {
                FilePath = reader.GetString(0),
                LineNumber = reader.GetInt32(1),
                Distance = reader.GetFloat(2)
            });
        }

        return results;
    }

    /// <summary>
    /// Clears all vectors from the index.
    /// </summary>
    public async Task ClearAsync()
    {
        if (!_initialized || _connection == null)
        {
            throw new InvalidOperationException("VectorIndexService must be initialized before use.");
        }

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM memories;";
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes all vectors from the index associated with a specific file.
    /// </summary>
    public async Task DeleteByFileAsync(string filePath)
    {
        if (!_initialized || _connection == null)
        {
            throw new InvalidOperationException("VectorIndexService must be initialized before use.");
        }

        ArgumentNullException.ThrowIfNull(filePath);

        using var command = _connection.CreateCommand();
        command.CommandText = "DELETE FROM memories WHERE file_path = $filePath;";
        command.Parameters.AddWithValue("$filePath", filePath);
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Result from a vector similarity search.
/// </summary>
public class VectorSearchResult
{
    public required string FilePath { get; set; }
    public required int LineNumber { get; set; }
    public required float Distance { get; set; }
}
