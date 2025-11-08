# 🧠 Recall - Memory MCP Server for AI Agents

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-29%20passing-brightgreen)](https://github.com/yourusername/recall)

Recall is an embedded, project-scoped Memory Capture Protocol (MCP) server that provides persistent, long-term semantic memory for AI agents. Built with .NET 9.0 and ONNX Runtime.

## ✨ Features

- **Persistent Memory**: Store and recall memories across sessions using JSONL files
- **Semantic Search**: Find relevant memories using AI-powered similarity search (384-dim embeddings)
- **Git-Friendly**: JSONL storage format works great with version control
- **Project-Scoped**: Each project has its own `.recall` directory
- **Fast Lookups**: Vector search index for quick semantic retrieval
- **Auto-Initialization**: System sets up on first use, no manual init required
- **Background Processing**: Embedding generation and indexing happen asynchronously
- **File Watcher**: Automatically re-indexes when JSONL files change

## 🏗️ Architecture

### Dual Storage System
- **Source of Truth**: JSONL files in `.recall/memories/YYYY-MM-DD/memories.jsonl`
  - Human-readable, git-friendly format
  - One memory per line
  - Version-controlled
- **Search Index**: SQLite database (`.recall/index.db`)
  - Disposable cache (can be rebuilt from JSONL)
  - Stores 384-dimensional embeddings
  - Git-ignored

### Components
- **EmbeddingService**: ONNX Runtime with all-MiniLM-L6-v2 model (GPU-accelerated when available)
- **JsonlStorageService**: Append-only JSONL storage with date-based partitioning
- **VectorIndexService**: SQLite-based vector search with brute-force k-NN (vec0 upgrade planned)
- **FileWatcherService**: Debounced file system watcher for automatic re-indexing
- **RecallTools**: MCP tool implementations with auto-initialization

## 🔧 MCP Tools

Recall provides **2 simple tools** - initialization happens automatically:

### `store`
Store a memory event for semantic recall.

**Parameters:**
- `type` (string): Memory type - `feature`, `bug-fix`, `decision`, `insight`, `observation`, `refactor`
- `source` (string): Source identifier - `agent`, `user`, `system`, `development-session`
- `content` (string): Memory content (2-4 sentences recommended)

**Example:**
```json
{
  "name": "store",
  "arguments": {
    "type": "decision",
    "source": "agent",
    "content": "Chose SQLite over PostgreSQL for vector storage. Rationale: Embedded database simplifies deployment and reduces dependencies. Trade-off: Less scalable but acceptable for single-project scope."
  }
}
```

### `recall`
Search for memories semantically similar to a query.

**Parameters:**
- `query` (string): Semantic search query
- `k` (integer, optional): Number of results to return (default: 5, max: 20)

**Example:**
```json
{
  "name": "recall",
  "arguments": {
    "query": "Why did we choose SQLite?",
    "k": 5
  }
}
```

**Response Format:**
```json
{
  "success": true,
  "query": "Why did we choose SQLite?",
  "resultsCount": 2,
  "memories": [
    {
      "type": "decision",
      "source": "agent",
      "content": "Chose SQLite over PostgreSQL...",
      "timestamp": "2025-11-08T12:00:00Z",
      "similarity": 0.87
    }
  ]
}
```

## 🚀 Installation

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Claude Code](https://claude.com/claude-code) or any MCP-compatible client

### Build from Source

```bash
# Clone the repository
git clone https://github.com/anortham/recall.git
cd recall

# Build the project
dotnet build --configuration Release

# Run tests to verify
dotnet test
```

**Note:** The ONNX model files (`model.onnx` - 86MB, `vocab.txt` - 232KB) are **automatically downloaded** from HuggingFace on first use. The files are not included in the repository to keep it lightweight. The download happens once during the first `store` or `recall` operation and takes about 30-60 seconds depending on your connection.

If you prefer to download manually or are in an offline environment, use the provided setup scripts:
```bash
# Windows
.\setup-model.ps1

# Linux/Mac
./setup-model.sh
```

### Claude Code Configuration

Add to your `.claude/mcp.json` or global MCP settings:

```json
{
  "mcpServers": {
    "recall": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/path/to/recall/Recall/Recall.csproj",
        "--configuration",
        "Release"
      ]
    }
  }
}
```

**Note:** Update the path to match your installation directory.

## 🧪 Testing

Run all tests:
```bash
dotnet test
```

Run with coverage:
```bash
dotnet test /p:CollectCoverage=true
```

Watch mode for TDD:
```bash
dotnet watch test
```

**Current Status:** 29/29 tests passing ✅

## 📦 Development

### Building

**Debug build** (for development with hot reload):
```bash
dotnet build --configuration Debug
```

**Release build** (optimized):
```bash
dotnet build --configuration Release
```

**Important:** When dogfooding (running the release build as an MCP server), only build debug versions to avoid file locking issues.

### Project Structure

```
Recall/
├── Recall/                      # Main MCP server project
│   ├── Models/                  # Data models (MemoryEvent)
│   ├── Services/                # Core services
│   │   ├── EmbeddingService.cs
│   │   ├── JsonlStorageService.cs
│   │   ├── VectorIndexService.cs
│   │   └── FileWatcherService.cs
│   ├── Tools/                   # MCP tool implementations
│   │   └── RecallTools.cs
│   ├── Assets/model/            # ONNX model files (87MB)
│   └── Program.cs               # Entry point
├── Recall.Tests/                # Unit tests (NUnit)
│   └── Unit/
├── .recall/                     # Memory storage (auto-created)
│   ├── memories/                # JSONL files (git-tracked)
│   ├── index.db                 # Vector index (git-ignored)
│   └── logs/                    # Server logs (git-ignored)
├── CLAUDE.md                    # Project context for Claude
├── PRD.md                       # Product requirements
└── README.md                    # You are here
```

## 🎯 Development Status

### ✅ Completed
- [x] MemoryEvent model with JSON serialization
- [x] JsonlStorageService with date-based partitioning
- [x] EmbeddingService with real ONNX Runtime (all-MiniLM-L6-v2)
- [x] VectorIndexService with brute-force k-NN search
- [x] FileWatcherService for automatic re-indexing
- [x] MCP server with stdio transport
- [x] Auto-initialization on first use
- [x] Background embedding/indexing
- [x] Comprehensive test coverage (29 tests)
- [x] Dependency injection with singleton services
- [x] Structured logging with Serilog

### 🚧 Planned Improvements
- [ ] Upgrade to sqlite-vec extension for faster vector search
- [ ] GPU acceleration support (CUDA/DirectML)
- [ ] Performance benchmarking suite
- [ ] CI/CD pipeline with GitHub Actions
- [ ] Pre-built binaries for releases
- [ ] Docker support

## 📝 License

MIT License - see [LICENSE](LICENSE) for details.

## 🤝 Contributing

Contributions welcome! This project follows TDD methodology:
1. Write failing test first
2. Implement minimum code to pass
3. Refactor while keeping tests green

See [CLAUDE.md](CLAUDE.md) for detailed development guidelines.

## 🙏 Acknowledgments

- [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [all-MiniLM-L6-v2](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2) by Sentence Transformers
- [sqlite-vec](https://github.com/asg017/sqlite-vec) by Alex Garcia
- [ONNX Runtime](https://onnxruntime.ai/) by Microsoft

---

**Built with ❤️ using Test-Driven Development**
