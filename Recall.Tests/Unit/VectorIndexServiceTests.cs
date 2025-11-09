using Microsoft.Data.Sqlite;

namespace Recall.Tests.Unit;

[TestFixture]
public class VectorIndexServiceTests
{
    private string _testDbPath = null!;
    private VectorIndexService _service = null!;

    [SetUp]
    public void Setup()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid()}.db");
        _service = new VectorIndexService(_testDbPath);
    }

    [TearDown]
    public void TearDown()
    {
        _service?.Dispose();

        // Clear connection pool to release file lock
        SqliteConnection.ClearAllPools();
        GC.Collect();
        GC.WaitForPendingFinalizers();

        if (File.Exists(_testDbPath))
        {
            File.Delete(_testDbPath);
        }
    }

    [Test]
    public async Task InitializeAsync_ShouldCreateDatabase()
    {
        // Act
        await _service.InitializeAsync();

        // Assert
        Assert.That(File.Exists(_testDbPath), Is.True);
    }

    [Test]
    public async Task InitializeAsync_ShouldCreateMemoriesTable()
    {
        // Act
        await _service.InitializeAsync();

        // Assert - verify table exists by querying it
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='memories';";
        var result = await command.ExecuteScalarAsync();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.ToString(), Is.EqualTo("memories"));
    }

    [Test]
    public async Task InitializeAsync_ShouldCreateVec0VirtualTable()
    {
        // Act
        await _service.InitializeAsync();

        // Assert - verify it's a virtual table using vec0, not a regular table
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = @"
            SELECT sql
            FROM sqlite_master
            WHERE type='table' AND name='memories';
        ";
        var tableDef = await command.ExecuteScalarAsync();

        Assert.That(tableDef, Is.Not.Null);
        var tableDefinition = tableDef!.ToString()!;

        // Verify it's a virtual table using vec0 extension
        Assert.That(tableDefinition, Does.Contain("VIRTUAL TABLE"));
        Assert.That(tableDefinition, Does.Contain("vec0"));
        Assert.That(tableDefinition, Does.Contain("embedding"));
    }

    [Test]
    public async Task InsertAsync_ShouldStoreVectorWithReference()
    {
        // Arrange
        await _service.InitializeAsync();
        var embedding = new float[384];
        for (int i = 0; i < 384; i++) embedding[i] = 0.1f;
        var filePath = ".recall/2025-11-08/memories.jsonl";
        var lineNumber = 5;

        // Act
        await _service.InsertAsync(embedding, filePath, lineNumber);

        // Assert - verify the row was inserted
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        await connection.OpenAsync();

        // Load vec0 extension for verification queries
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var extensionPath = Path.Combine(baseDir, "Assets", "sqlite-vec", "vec0");
        connection.EnableExtensions(true);
        connection.LoadExtension(extensionPath);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM memories;";
        var count = (long)(await command.ExecuteScalarAsync())!;

        Assert.That(count, Is.EqualTo(1));
    }

    [Test]
    public async Task InsertAsync_ShouldStoreMultipleVectors()
    {
        // Arrange
        await _service.InitializeAsync();
        var embedding1 = new float[384];
        var embedding2 = new float[384];
        var embedding3 = new float[384];

        // Act
        await _service.InsertAsync(embedding1, "file1.jsonl", 0);
        await _service.InsertAsync(embedding2, "file2.jsonl", 1);
        await _service.InsertAsync(embedding3, "file3.jsonl", 2);

        // Assert
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        await connection.OpenAsync();

        // Load vec0 extension for verification queries
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var extensionPath = Path.Combine(baseDir, "Assets", "sqlite-vec", "vec0");
        connection.EnableExtensions(true);
        connection.LoadExtension(extensionPath);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM memories;";
        var count = (long)(await command.ExecuteScalarAsync())!;

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public async Task SearchAsync_ShouldReturnKNearestNeighbors()
    {
        // Arrange
        await _service.InitializeAsync();

        // Insert 5 vectors
        for (int i = 0; i < 5; i++)
        {
            var embedding = new float[384];
            for (int j = 0; j < 384; j++)
            {
                embedding[j] = i * 0.1f; // Different vectors
            }
            await _service.InsertAsync(embedding, $"file{i}.jsonl", i);
        }

        var queryEmbedding = new float[384];
        for (int i = 0; i < 384; i++) queryEmbedding[i] = 0.15f; // Close to first vectors

        // Act
        var results = await _service.SearchAsync(queryEmbedding, k: 3);

        // Assert
        Assert.That(results, Is.Not.Null);
        Assert.That(results, Has.Count.EqualTo(3), "Should return exactly k results");
    }

    [Test]
    public async Task SearchAsync_ShouldReturnResultsOrderedByDistance()
    {
        // Arrange
        await _service.InitializeAsync();

        // For cosine distance, we need vectors that differ in DIRECTION (angle), not just magnitude.
        // Create vectors pointing in different directions in the 384-dimensional space.

        var embedding1 = new float[384]; // Far from query - points in opposite direction
        var embedding2 = new float[384]; // Close to query - points in similar direction
        var embedding3 = new float[384]; // Medium distance - points in somewhat different direction

        // embedding1: Emphasize second half of dimensions (opposite of query)
        for (int i = 0; i < 384; i++)
        {
            embedding1[i] = i < 192 ? 0.1f : 1.0f;
        }

        // embedding2: Emphasize first half of dimensions (similar to query)
        for (int i = 0; i < 384; i++)
        {
            embedding2[i] = i < 192 ? 1.0f : 0.1f;
        }

        // embedding3: Balanced across both halves (medium angle from query)
        for (int i = 0; i < 384; i++)
        {
            embedding3[i] = 0.5f;
        }

        await _service.InsertAsync(embedding1, "far.jsonl", 0);
        await _service.InsertAsync(embedding2, "close.jsonl", 1);
        await _service.InsertAsync(embedding3, "medium.jsonl", 2);

        // Query: Strong emphasis on first half (similar direction to embedding2)
        var queryEmbedding = new float[384];
        for (int i = 0; i < 384; i++)
        {
            queryEmbedding[i] = i < 192 ? 0.9f : 0.2f;
        }

        // Act
        var results = await _service.SearchAsync(queryEmbedding, k: 3);

        // Assert - results should be ordered by cosine distance (closest angle first)
        Assert.That(results[0].FilePath, Is.EqualTo("close.jsonl"), "Closest vector (similar direction) should be first");
        Assert.That(results[1].FilePath, Is.EqualTo("medium.jsonl"), "Medium vector should be second");
        Assert.That(results[2].FilePath, Is.EqualTo("far.jsonl"), "Far vector (opposite direction) should be last");
        Assert.That(results, Has.Count.EqualTo(3), "Should return all 3 results");
    }

    [Test]
    public async Task SearchAsync_ShouldReturnFilePathAndLineNumber()
    {
        // Arrange
        await _service.InitializeAsync();
        var embedding = new float[384];
        var expectedPath = ".recall/2025-11-08/memories.jsonl";
        var expectedLine = 42;

        await _service.InsertAsync(embedding, expectedPath, expectedLine);

        // Act
        var results = await _service.SearchAsync(embedding, k: 1);

        // Assert
        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].FilePath, Is.EqualTo(expectedPath));
        Assert.That(results[0].LineNumber, Is.EqualTo(expectedLine));
    }

    [Test]
    public async Task SearchAsync_ShouldReturnDistanceScore()
    {
        // Arrange
        await _service.InitializeAsync();
        var embedding = new float[384];
        for (int i = 0; i < 384; i++) embedding[i] = 1.0f;

        await _service.InsertAsync(embedding, "test.jsonl", 0);

        // Act - search with same vector (perfect match)
        var results = await _service.SearchAsync(embedding, k: 1);

        // Assert
        Assert.That(results[0].Distance, Is.GreaterThanOrEqualTo(0));
        Assert.That(results[0].Distance, Is.LessThanOrEqualTo(2.0)); // Reasonable distance range
    }

    [Test]
    public async Task ClearAsync_ShouldRemoveAllVectors()
    {
        // Arrange
        await _service.InitializeAsync();
        await _service.InsertAsync(new float[384], "file1.jsonl", 0);
        await _service.InsertAsync(new float[384], "file2.jsonl", 1);

        // Act
        await _service.ClearAsync();

        // Assert
        using var connection = new SqliteConnection($"Data Source={_testDbPath}");
        await connection.OpenAsync();

        // Load vec0 extension for verification queries
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var extensionPath = Path.Combine(baseDir, "Assets", "sqlite-vec", "vec0");
        connection.EnableExtensions(true);
        connection.LoadExtension(extensionPath);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM memories;";
        var count = (long)(await command.ExecuteScalarAsync())!;

        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void InsertAsync_ShouldThrowIfNotInitialized()
    {
        // Arrange
        var embedding = new float[384];

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _service.InsertAsync(embedding, "test.jsonl", 0));
    }
}
