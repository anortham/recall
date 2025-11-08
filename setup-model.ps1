# Setup script for downloading ONNX model files
# Downloads all-MiniLM-L6-v2 model from HuggingFace

$ErrorActionPreference = "Stop"

Write-Host "🧠 Recall MCP Server - Model Setup" -ForegroundColor Cyan
Write-Host ""

# Define paths
$modelDir = Join-Path $PSScriptRoot "Recall\Assets\model"
$modelPath = Join-Path $modelDir "model.onnx"
$vocabPath = Join-Path $modelDir "vocab.txt"

# Create directory if it doesn't exist
if (-not (Test-Path $modelDir)) {
    Write-Host "📁 Creating model directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $modelDir -Force | Out-Null
}

# Check if files already exist
$modelExists = Test-Path $modelPath
$vocabExists = Test-Path $vocabPath

if ($modelExists -and $vocabExists) {
    Write-Host "✅ Model files already exist!" -ForegroundColor Green
    Write-Host "   - model.onnx: $('{0:N2}' -f ((Get-Item $modelPath).Length / 1MB)) MB"
    Write-Host "   - vocab.txt: $('{0:N2}' -f ((Get-Item $vocabPath).Length / 1KB)) KB"
    Write-Host ""
    $overwrite = Read-Host "Do you want to re-download? (y/N)"
    if ($overwrite -ne "y" -and $overwrite -ne "Y") {
        Write-Host "Setup complete. Using existing model files." -ForegroundColor Green
        exit 0
    }
}

# HuggingFace URLs
$baseUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/resolve/main/onnx"
$modelUrl = "$baseUrl/model.onnx"
$vocabUrl = "$baseUrl/vocab.txt"

Write-Host "📥 Downloading model files from HuggingFace..." -ForegroundColor Yellow
Write-Host ""

# Download model.onnx (86MB - this will take a moment)
Write-Host "⏳ Downloading model.onnx (86 MB)..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri $modelUrl -OutFile $modelPath -UseBasicParsing
    Write-Host "✅ model.onnx downloaded" -ForegroundColor Green
} catch {
    Write-Host "❌ Failed to download model.onnx: $_" -ForegroundColor Red
    exit 1
}

# Download vocab.txt (232KB)
Write-Host "⏳ Downloading vocab.txt (232 KB)..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri $vocabUrl -OutFile $vocabPath -UseBasicParsing
    Write-Host "✅ vocab.txt downloaded" -ForegroundColor Green
} catch {
    Write-Host "❌ Failed to download vocab.txt: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✅ Setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Model files installed to:" -ForegroundColor Cyan
Write-Host "  $modelDir"
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Build the project: dotnet build --configuration Release"
Write-Host "  2. Run tests: dotnet test"
Write-Host "  3. Configure MCP server in Claude Code"
Write-Host ""
