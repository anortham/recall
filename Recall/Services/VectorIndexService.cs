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
    /// Initializes the database and creates the memories table.
    /// For now uses regular SQLite table; TODO: upgrade to vec0 extension.
    /// </summary>
    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection($"Data Source={_databasePath}");
        await _connection.OpenAsync();

        // TODO: Load vec0 extension when available
        // For now, use regular SQLite table with BLOB for vectors
        using var command = _connection.CreateCommand();
        command.CommandText = @"
            CREATE TABLE IF NOT EXISTS memories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL,
                line_number INTEGER NOT NULL,
                embedding BLOB NOT NULL
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

        // Serialize float[] to bytes
        var embeddingBytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, embeddingBytes, 0, embeddingBytes.Length);

        using var command = _connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO memories (file_path, line_number, embedding)
            VALUES ($filePath, $lineNumber, $embedding);
        ";
        command.Parameters.AddWithValue("$filePath", filePath);
        command.Parameters.AddWithValue("$lineNumber", lineNumber);
        command.Parameters.AddWithValue("$embedding", embeddingBytes);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Searches for the k nearest neighbors to the query embedding.
    /// Uses brute-force cosine distance for now; TODO: upgrade to vec0 for speed.
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

        // Brute-force k-NN: load all vectors, compute distances, return top k
        var results = new List<VectorSearchResult>();

        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT id, file_path, line_number, embedding FROM memories;";

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var filePath = reader.GetString(1);
            var lineNumber = reader.GetInt32(2);
            var embeddingBytes = (byte[])reader["embedding"];

            var embedding = new float[384];
            Buffer.BlockCopy(embeddingBytes, 0, embedding, 0, embeddingBytes.Length);

            var distance = ComputeCosineDistance(queryEmbedding, embedding);

            results.Add(new VectorSearchResult
            {
                FilePath = filePath,
                LineNumber = lineNumber,
                Distance = distance
            });
        }

        // Sort by distance (ascending) and take top k
        return results.OrderBy(r => r.Distance).Take(k).ToList();
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

    private float ComputeCosineDistance(float[] a, float[] b)
    {
        // Compute cosine distance using the full formula: 1 - (dot(a,b) / (||a|| * ||b||))
        // This works correctly for both normalized and non-normalized vectors.
        // While all-MiniLM-L6-v2 produces normalized vectors in production,
        // using the full formula ensures correctness in all cases (including tests).

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            magnitudeA += a[i] * a[i];
            magnitudeB += b[i] * b[i];
        }

        magnitudeA = MathF.Sqrt(magnitudeA);
        magnitudeB = MathF.Sqrt(magnitudeB);

        // Handle zero vectors to avoid division by zero
        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 1.0f; // Maximum distance for zero vectors
        }

        float cosineSimilarity = dotProduct / (magnitudeA * magnitudeB);

        // Clamp to [-1, 1] to prevent floating point inaccuracies
        cosineSimilarity = Math.Max(-1.0f, Math.Min(1.0f, cosineSimilarity));

        return 1.0f - cosineSimilarity;
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
