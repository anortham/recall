#!/bin/bash
# Setup script for downloading ONNX model files
# Downloads all-MiniLM-L6-v2 model from HuggingFace

set -e

echo "🧠 Recall MCP Server - Model Setup"
echo ""

# Define paths
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
MODEL_DIR="$SCRIPT_DIR/Recall/Assets/model"
MODEL_PATH="$MODEL_DIR/model.onnx"
VOCAB_PATH="$MODEL_DIR/vocab.txt"

# Create directory if it doesn't exist
if [ ! -d "$MODEL_DIR" ]; then
    echo "📁 Creating model directory..."
    mkdir -p "$MODEL_DIR"
fi

# Check if files already exist
if [ -f "$MODEL_PATH" ] && [ -f "$VOCAB_PATH" ]; then
    echo "✅ Model files already exist!"
    echo "   - model.onnx: $(du -h "$MODEL_PATH" | cut -f1)"
    echo "   - vocab.txt: $(du -h "$VOCAB_PATH" | cut -f1)"
    echo ""
    read -p "Do you want to re-download? (y/N): " -n 1 -r
    echo ""
    if [[ ! $REPLY =~ ^[Yy]$ ]]; then
        echo "Setup complete. Using existing model files."
        exit 0
    fi
fi

# HuggingFace URLs
BASE_URL="https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx"
MODEL_URL="$BASE_URL/model.onnx"
VOCAB_URL="$BASE_URL/vocab.txt"

echo "📥 Downloading model files from HuggingFace..."
echo ""

# Check if curl or wget is available
if command -v curl &> /dev/null; then
    DOWNLOAD_CMD="curl -L -o"
elif command -v wget &> /dev/null; then
    DOWNLOAD_CMD="wget -O"
else
    echo "❌ Error: Neither curl nor wget found. Please install one of them."
    exit 1
fi

# Download model.onnx (86MB - this will take a moment)
echo "⏳ Downloading model.onnx (86 MB)..."
if $DOWNLOAD_CMD "$MODEL_PATH" "$MODEL_URL"; then
    echo "✅ model.onnx downloaded"
else
    echo "❌ Failed to download model.onnx"
    exit 1
fi

# Download vocab.txt (232KB)
echo "⏳ Downloading vocab.txt (232 KB)..."
if $DOWNLOAD_CMD "$VOCAB_PATH" "$VOCAB_URL"; then
    echo "✅ vocab.txt downloaded"
else
    echo "❌ Failed to download vocab.txt"
    exit 1
fi

echo ""
echo "✅ Setup complete!"
echo ""
echo "Model files installed to:"
echo "  $MODEL_DIR"
echo ""
echo "Next steps:"
echo "  1. Build the project: dotnet build --configuration Release"
echo "  2. Run tests: dotnet test"
echo "  3. Configure MCP server in Claude Code"
echo ""
