using AllMiniLmL6V2Sharp;
using AllMiniLmL6V2Sharp.Tokenizer;
using Microsoft.ML.OnnxRuntime;
using Serilog;

namespace Recall;

/// <summary>
/// Service for generating embeddings from text using the all-MiniLM-L6-v2 ONNX model.
/// Produces 384-dimensional normalized vectors for semantic similarity search.
/// </summary>
public class EmbeddingService : IDisposable
{
    /// <summary>
    /// The dimensionality of embeddings produced by all-MiniLM-L6-v2.
    /// </summary>
    public const int EmbeddingDimension = 384;

    private readonly AllMiniLmL6V2Embedder _embedder;
    private bool _disposed = false;
    private static bool _providerLogged = false;
    private static readonly object _logLock = new();

    public EmbeddingService(string? modelPath = null, string? vocabPath = null)
    {
        // Default to Assets/model directory
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var defaultModelPath = Path.Combine(baseDir, "Assets", "model", "model.onnx");
        var defaultVocabPath = Path.Combine(baseDir, "Assets", "model", "vocab.txt");

        var actualModelPath = modelPath ?? defaultModelPath;
        var actualVocabPath = vocabPath ?? defaultVocabPath;

        // Ensure model files exist - download if missing
        EnsureModelFilesExist(actualModelPath, actualVocabPath).GetAwaiter().GetResult();

        // Log available ONNX execution providers (only once)
        lock (_logLock)
        {
            if (!_providerLogged)
            {
                LogAvailableProviders();
                _providerLogged = true;
            }
        }

        // Create tokenizer with explicit vocab path
        var tokenizer = new BertTokenizer(actualVocabPath);

        _embedder = new AllMiniLmL6V2Embedder(
            modelPath: actualModelPath,
            tokenizer: tokenizer
        );
    }

    private async Task EnsureModelFilesExist(string modelPath, string vocabPath)
    {
        var modelExists = File.Exists(modelPath);
        var vocabExists = File.Exists(vocabPath);

        if (modelExists && vocabExists)
        {
            return; // Both files exist, nothing to do
        }

        Log.Information("📥 Model files not found, downloading from HuggingFace...");

        // Create directory if it doesn't exist
        var modelDir = Path.GetDirectoryName(modelPath);
        if (modelDir != null && !Directory.Exists(modelDir))
        {
            Directory.CreateDirectory(modelDir);
        }

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5); // 86MB download may take time

        const string modelBaseUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx";
        const string vocabBaseUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main";

        try
        {
            // Download model.onnx if missing (from /onnx subfolder)
            if (!modelExists)
            {
                Log.Information("⏳ Downloading model.onnx (86 MB)...");
                var modelBytes = await httpClient.GetByteArrayAsync($"{modelBaseUrl}/model.onnx");
                await File.WriteAllBytesAsync(modelPath, modelBytes);
                Log.Information("✅ model.onnx downloaded successfully");
            }

            // Download vocab.txt if missing (from main branch root, not /onnx)
            if (!vocabExists)
            {
                Log.Information("⏳ Downloading vocab.txt (232 KB)...");
                var vocabBytes = await httpClient.GetByteArrayAsync($"{vocabBaseUrl}/vocab.txt");
                await File.WriteAllBytesAsync(vocabPath, vocabBytes);
                Log.Information("✅ vocab.txt downloaded successfully");
            }

            Log.Information("✅ Model files ready");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download model files from HuggingFace");
            throw new InvalidOperationException(
                "Failed to download ONNX model files. Please check your internet connection " +
                "or manually download from https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/tree/main/onnx",
                ex);
        }
    }

    private void LogAvailableProviders()
    {
        try
        {
            var availableProviders = OrtEnv.Instance().GetAvailableProviders();
            var providers = string.Join(", ", availableProviders);

            Log.Information("ONNX Runtime available providers: {Providers}", providers);

            // Highlight GPU providers
            if (availableProviders.Contains("CUDAExecutionProvider"))
            {
                Log.Information("✅ GPU acceleration ENABLED: CUDA");
            }
            else if (availableProviders.Contains("DmlExecutionProvider"))
            {
                Log.Information("✅ GPU acceleration ENABLED: DirectML (Windows)");
            }
            else if (availableProviders.Contains("TensorrtExecutionProvider"))
            {
                Log.Information("✅ GPU acceleration ENABLED: TensorRT");
            }
            else
            {
                Log.Warning("⚠️ GPU acceleration DISABLED - using CPU only");
                Log.Information("💡 To enable GPU: Install CUDA toolkit or use DirectML provider");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to detect ONNX execution providers");
        }
    }

    /// <summary>
    /// Generates a normalized embedding vector from input text.
    /// </summary>
    /// <param name="text">The input text to embed.</param>
    /// <returns>A 384-dimensional normalized float array.</returns>
    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Run ONNX inference on thread pool to avoid blocking
        return await Task.Run(() => _embedder.GenerateEmbedding(text).ToArray());
    }

    /// <summary>
    /// Generates normalized embedding vectors for multiple texts in a single GPU batch.
    /// Much faster than calling GenerateEmbeddingAsync in a loop.
    /// </summary>
    /// <param name="texts">The input texts to embed.</param>
    /// <returns>An array of 384-dimensional normalized float arrays.</returns>
    public async Task<float[][]> GenerateEmbeddingBatchAsync(string[] texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Length == 0)
        {
            return Array.Empty<float[]>();
        }

        // Run ONNX batch inference on thread pool to avoid blocking
        return await Task.Run(() =>
        {
            var embeddings = _embedder.GenerateEmbeddings(texts);
            return embeddings.Select(e => e.ToArray()).ToArray();
        });
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _embedder?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
