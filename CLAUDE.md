# Recall MCP Server - Project Context

## Project Overview

**Recall** is an embedded, project-scoped Memory Capture Protocol (MCP) server for AI agents. It provides persistent, long-term semantic memory through a dual-storage architecture:
1. **Source of Truth**: JSONL files (`.recall/YYYY-MM-DD/memories.jsonl`) - Git-friendly, human-readable
2. **Search Index**: sqlite-vec database (`.recall/index.db`) - GPU-accelerated semantic search

## Development Philosophy

### TDD is Non-Negotiable
This is a **TDD shop**. We write tests FIRST, then implement. No exceptions.

**The TDD Loop:**
1. Write a failing test that defines desired behavior
2. Implement the MINIMUM code to make it pass
3. Refactor while keeping tests green
4. Repeat

### Build Configuration

**CRITICAL - READ THIS:**

- ⚠️ **NEVER BUILD RELEASE** - The release build is LOCKED by the running MCP server
- We are **dogfooding the RELEASE version** in Claude Code during this session
- **ONLY BUILD DEBUG**: `dotnet build --configuration Debug`
- Building release will fail with "file is being used by another process"
- Only the user can build release (by restarting Claude Code first)

**Why this matters:**
- Release build runs as the MCP server in this Claude Code session
- The .exe file is locked and cannot be overwritten while running
- Debug builds are fine - they don't conflict with the running server
- If you try to build release, you will waste time waiting for it to fail

## Technical Stack

| Component | Technology | Version/Notes |
|-----------|-----------|---------------|
| **Framework** | .NET 8 | Latest LTS |
| **Host** | .NET Generic Host | For DI, logging, lifecycle |
| **MCP SDK** | `ModelContextProtocol` | Prerelease, stdio transport |
| **Storage** | JSONL files | `.recall/YYYY-MM-DD/memories.jsonl` |
| **Vector DB** | sqlite-vec | Embedded, via `Microsoft.Data.Sqlite` |
| **Embeddings** | `Microsoft.ML.OnnxRuntime.Gpu` | CUDA 12.x / DirectML |
| **Model** | all-MiniLM-L6-v2 (ONNX) | 384 dimensions, HuggingFace |
| **Testing** | NUnit | Unit and integration tests |

## Architecture Patterns

### Dependency Injection
Use .NET Generic Host's built-in DI:
- Services are registered in `Program.cs`
- Inject interfaces, not concrete types
- Favor constructor injection

### Async/Await
- All I/O operations must be async
- Use `Task` for async operations, `ValueTask` for hot paths
- Background tasks use `IHostedService` or `BackgroundService`

### Error Handling
- Validate inputs at boundaries (MCP tool handlers)
- Use Result pattern or exceptions (decide early)
- Log errors with structured logging (ILogger)

## Critical Design Decisions

### 1. JSONL as Source of Truth
- The `.recall/YYYY-MM-DD/memories.jsonl` files are **immutable**
- The `index.db` is a **disposable cache** that can always be rebuilt
- Git-friendly: JSONL is version-controlled, index.db is .gitignored

### 2. Background Processing
- `store` tool returns immediately (< 100ms target)
- Embedding + indexing happens asynchronously in background
- Use `System.Threading.Channels` for work queue

### 3. Embedding Consistency
- The **exact same ONNX model** must be used for both `store` and `recall`
- Model path should be configurable
- Log which execution provider (CUDA/DirectML/CPU) is active on startup

### 4. sqlite-vec Integration
- Load the native extension (`vec0.dll` / `vec0.so`) at runtime
- Create virtual table: `CREATE VIRTUAL TABLE memories USING vec0(embedding(384))`
- Store references to JSONL file path + line number in the index

## Testing Strategy

### Unit Tests
- Test individual components in isolation
- Mock external dependencies (file system, database)
- Fast, deterministic, no I/O

### Integration Tests
- Test the full stack with real file system and database
- Use temporary directories for isolation
- Clean up after each test

### Test Organization
```
Recall.Tests/
├── Unit/
│   ├── EmbeddingServiceTests.cs
│   ├── MemoryStoreTests.cs
│   └── VectorIndexTests.cs
├── Integration/
│   ├── StoreToolTests.cs
│   ├── RecallToolTests.cs
│   └── RebuildToolTests.cs
└── Fixtures/
    └── TestData.cs
```

## Key Performance Targets

- `store` tool response: **< 100ms**
- `recall` tool response: **< 500ms** (k=5 search)
- Embedding generation: Use GPU when available, fallback to CPU

## Security & Safety

- **No external network calls** (except model download during setup)
- **Project-scoped**: Server operates only within `.recall` directory
- **No authentication**: Assumed to be local, not internet-facing
- **Input validation**: Sanitize all MCP tool inputs

## Common Pitfalls to Avoid

1. **Guessing at APIs**: Always verify symbols with Julie MCP before writing code
2. **Skipping tests**: Write the test FIRST, even for "simple" code
3. **Overengineering**: Implement only what's needed to pass the test
4. **Ignoring async**: All I/O must be async to avoid blocking
5. **Hardcoding paths**: Make file paths configurable and testable

## Useful Commands

```bash
# Build debug version (for dogfooding)
dotnet build --configuration Debug

# Run all tests
dotnet test

# Run tests with coverage
dotnet test /p:CollectCoverage=true

# Watch mode for TDD
dotnet watch test

# Run the MCP server (stdio mode)
dotnet run --project Recall --configuration Debug
```

## References

- [MCP C# SDK Docs](https://github.com/modelcontextprotocol/csharp-sdk)
- [sqlite-vec Repository](https://github.com/asg017/sqlite-vec)
- [ONNX Runtime C# Docs](https://onnxruntime.ai/docs/api/csharp/api/)
- [all-MiniLM-L6-v2 Model](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)

## Notes from Development

### Session 2025-11-08: Initial Build & Dogfooding Success

**What We Built:**
- Complete TDD implementation: 29 tests, all GREEN
- 4 core services: MemoryEvent, JsonlStorageService, EmbeddingService, VectorIndexService
- Full MCP server with 4 tools: init, store, recall, rebuild
- Successfully dogfooded in this Claude Code session

**Key Learnings:**

1. **MCP C# SDK Patterns:**
   - Attributes are in `ModelContextProtocol.Server` namespace, not base
   - Use `[McpServerToolType]` on class and `[McpServerTool]` on methods
   - Logging MUST go to stderr - stdout is reserved for stdio protocol
   - `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` auto-discovers tools

2. **SQLite Connection Pooling:**
   - Must call `SqliteConnection.ClearAllPools()` in test teardown
   - Add `GC.Collect()` and `GC.WaitForPendingFinalizers()` to release file locks
   - Otherwise tests fail with "file is being used by another process"

3. **TDD Placeholder Strategy:**
   - Built deterministic hash-based embeddings to enable full testing
   - Interface-compatible with future ONNX upgrade
   - Allows end-to-end validation without external dependencies
   - Tests prove architecture works; production upgrade is low-risk

4. **JSONL Design Validation:**
   - Date-stamped directories (`.recall/YYYY-MM-DD/memories.jsonl`) work perfectly
   - Single-line JSON format is Git-friendly and easy to parse
   - Line numbers provide stable references for vector index
   - Can rebuild index from JSONL at any time

5. **Build Configuration:**
   - Running RELEASE build as MCP server for dogfooding
   - Only build DEBUG during development to avoid restarting MCP server
   - `dotnet build --configuration Debug` is the safe command

**Gotchas:**

- Don't forget `Description` attribute on parameters - helps with tool discoverability
- JSON responses should be pretty-printed for better debugging (`WriteIndented = true`)
- Vector index needs to be initialized before use - throw `InvalidOperationException` if not
- Cosine distance with zero vectors returns 1.0 (maximum distance) - avoid zero query vectors

**What's Next:**
- Upgrade to real ONNX embeddings (all-MiniLM-L6-v2)
- Add vec0 extension for faster vector search
- Background processing queue for async indexing
- Performance benchmarking with real workloads
