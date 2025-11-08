# PRD: "Recall" MCP Server

| Status | Version | Owner | Last Updated |
| :--- | :--- | :--- | :--- |
| Draft | 1.1 | [Your Name] | 2025-11-08 |

## 1. Overview

**"Recall"** is an embedded, project-scoped Memory Capture Protocol (MCP) server. It runs as a `stdio` daemon, managed by the official `modelcontextprotocol/csharp-sdk`. Its purpose is to provide a persistent, long-term semantic memory for AI agents operating within a specific codebase or project.

It features a "dual-storage" architecture:
1.  **Source of Truth:** Raw "memories" are stored as human-readable, Git-merge-friendly **JSONL files** in a `.recall` directory.
2.  **Search Index:** Embeddings of these memories are stored in an **embedded `sqlite-vec` database** (`.recall/index.db`) for high-speed semantic search.

This design allows developers to check agent memories directly into source control while providing the agent with a powerful, GPU-accelerated RAG (Retrieval-Augmented Generation) capability.

## 2. Problem Statement

AI agents are constrained by limited context windows, causing them to "forget" past interactions and context. Existing memory solutions often fall into two non-ideal camps:
1.  **Simple text search:** In-memory or file-based text search is not "smart" and cannot retrieve memories based on conceptual meaning, only on keyword matches.
2.  **External databases:** Client-server vector databases (like Qdrant, Chroma, Weaveate) are heavy, require separate server processes, and add friction to local development. They are not "project-scoped" and their data cannot be version-controlled with the codebase.

Developers need a zero-friction, project-scoped, and version-control-friendly system to give their agents a robust, semantic long-term memory.

## 3. Goals

* **Goal 1: Enable Semantic Recall:** The `recall` tool must retrieve memories based on conceptual meaning, not just keywords.
* **Goal 2: Ensure Version-Controlled Memories:** The `store` tool must write memories to a human-readable, merge-conflict-resistant format (JSONL) that can be checked into Git.
* **Goal 3: Deliver a Zero-Friction Setup:** The entire system must be self-contained within a project's `.recall` directory. It must not require any external database servers or services.
* **Goal 4: Provide High-Performance Tooling:** The system must leverage available hardware acceleration (NVIDIA CUDA / Windows DirectML) for the computationally expensive embedding process.

## 4. Personas

* **AI Agent (Primary User):** The automated consumer of the `store` and `recall` tools via the MCP protocol. Needs fast, reliable JSON responses to augment its reasoning loop.
* **Developer (Secondary User):** The human who installs/configures `Recall` in their project. Benefits from the Git integration, data visibility (JSONL), and project-scoped nature of the tool.

## 5. Core Features

### Epic 1: `store` Tool

**User Story:** As an AI Agent, I can send a new "memory" to the `store` tool so that it is permanently recorded and I can recall it later.

**Acceptance Criteria:**
* **MCP Tool:** `store`
* **Input:** A JSON message handled by the SDK, containing the `MemoryEvent` payload.
    ```json
    {
      "command": "store",
      "payload": {
        "type": "chat_message",
        "source": "user_a",
        "content": "The user seems frustrated about the slow API response time.",
        "timestamp": "2025-11-08T10:30:00Z"
      }
    }
    ```
* **File Storage (Source of Truth):**
    * The `MemoryEvent` is appended as a new line to a JSONL file.
    * The file path is date-stamped: `.recall/YYYY-MM-DD/memories.jsonl`.
    * The directory is created if it does not exist.
* **Embedding Generation:**
    * The `content` field is passed to the ONNX embedding service.
    * This service uses `Microsoft.ML.OnnxRuntime.Gpu` to leverage CUDA/DirectML if available, falling back to CPU.
* **Index Storage (Search):**
    * The generated vector is inserted into the `memories` table in the `.recall/index.db` (sqlite-vec) database.
    * The database record **must** include a reference (e.g., file path and line number) back to the event's location in the JSONL file.
* **Response:** The tool handler returns an "accepted" response immediately, queueing the embedding/indexing as a background task.
    ```json
    { "status": "ok", "message": "Memory queued for processing" }
    ```

---

### Epic 2: `recall` Tool

**User Story:** As an AI Agent, I can send a natural language query to the `recall` tool to find the most relevant memories from my past.

**Acceptance Criteria:**
* **MCP Tool:** `recall`
* **Input:** A JSON message handled by the SDK.
    ```json
    {
      "command": "recall",
      "payload": {
        "query": "user frustration about api speed",
        "k": 5
      }
    }
    ```
* **Embedding:** The `query` string is vectorized *using the exact same ONNX embedding model* used for the `store` tool.
* **Search:**
    * The `sqlite-vec` database is queried to find the `k` (or default 5) most similar vectors.
    * The search returns the `k` references (file path, line number) to the source of truth.
* **Retrieval:**
    * The system opens the referenced JSONL files.
    * It reads the specific lines corresponding to the search results.
    * It parses the JSON text into `MemoryEvent` objects.
* **Response:** The tool handler returns a `200 OK` with a JSON array of the full, original `MemoryEvent` objects.
    ```json
    {
      "status": "ok",
      "data": [
        { "type": "chat_message", "source": "user_a", ... }
      ]
    }
    ```

---

### Epic 3: System Initialization & Maintenance Tools

**User Story:** As a Developer, I can easily initialize and maintain the memory system within my project.

**Acceptance Criteria:**
* **`init` Tool:**
    * **MCP Tool:** `init`
    * Creates the `.recall` directory.
    * Creates the `.recall/index.db` file.
    * Runs the SQL `CREATE VIRTUAL TABLE IF NOT EXISTS memories USING vec0(embedding(384));` to initialize the `sqlite-vec` table (embedding size must be configurable).
    * Creates a `.gitignore` file *inside* `.recall` with the content: `index.db` (This is critical: we version the JSONL, not the index).
* **`rebuild` Tool:**
    * **MCP Tool:** `rebuild`
    * Clears the `index.db` and rebuilds it from scratch by reading all `.jsonl` files in the `.recall` directory.
    * This ensures the index can always be regenerated from the Git-controlled source of truth.

## 6. Technical Stack

| Component | Technology | Rationale |
| :--- | :--- | :--- |
| **Language/Framework** | C# (.NET 8) | Developer's preferred stack, fast dev loop. |
| **Host** | .NET Generic Host | Standard for daemon/console apps, provides DI, logging. |
| **MCP SDK** | `ModelContextProtocol` | Official C# SDK for handling `stdio` MCP communication. |
| **Raw Data Storage** | **JSONL** Files | Human-readable, merge-friendly, Git-compatible. |
| **Vector Database** | **`sqlite-vec`** | Embedded, file-based, project-scoped. No external server. |
| **DB Driver** | `Microsoft.Data.Sqlite` | Standard library for loading/querying SQLite. |
| **Embedding Engine** | `Microsoft.ML.OnnxRuntime.Gpu` | First-party package for cross-platform (CUDA/DirectML) GPU-accelerated ONNX models. |
| **Embedding Model** | ONNX-formatted (e.g., `all-MiniLM-L6-v2`) | Must be a single model used for both storing and recalling. |

## 7. Non-Functional Requirements

* **Performance:**
    * `store` tool response: Must return `< 100ms`. Embedding/indexing is done in the background.
    * `recall` tool response: Must return `< 500ms` for a `k=5` search.
* **Hardware:** The server must log which execution provider (CUDA, DirectML, or CPU) is being used on startup.
* **Portability:** The service must be runnable on Windows and Linux. This requires bundling the native `sqlite-vec` binaries (e.g., `vec0.dll`, `vec0.so`) and loading the correct one at runtime.
* **Data Integrity:** The JSONL files are the immutable source of truth. The `index.db` is a disposable cache that can be rebuilt at any time.

## 8. Out of Scope

* **A UI:** This is a headless MCP server, not a visual application.
* **Authentication:** The server is assumed to be project-local and not exposed to the public internet.
* **Multi-Tenancy:** The entire server is scoped to a single project.