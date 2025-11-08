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
