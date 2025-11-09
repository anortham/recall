using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Recall;
using Recall.Commands;
using Serilog;
using Serilog.Events;

// Handle CLI commands before starting MCP server
if (args.Length > 0)
{
    var command = args[0].ToLowerInvariant();

    if (command == "init")
    {
        return await InitCommand.ExecuteAsync(args);
    }
    else if (command == "--help" || command == "-h")
    {
        PrintUsage();
        return 0;
    }
    else if (command == "--version" || command == "-v")
    {
        Console.WriteLine("Recall MCP Server v0.1.0");
        return 0;
    }
    else
    {
        Console.Error.WriteLine($"Unknown command: {command}");
        PrintUsage();
        return 1;
    }
}

static void PrintUsage()
{
    Console.WriteLine("Recall MCP Server - Embedded semantic memory for AI agents");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  recall-mcp              Start MCP server (stdio mode)");
    Console.WriteLine("  recall-mcp init         Initialize project with commands and hooks");
    Console.WriteLine("  recall-mcp --help       Show this help message");
    Console.WriteLine("  recall-mcp --version    Show version information");
    Console.WriteLine();
    Console.WriteLine("For 'init' command options:");
    InitCommand.PrintUsage();
}

// Configure Serilog BEFORE building the host
// CRITICAL: Write to stderr and file, NEVER stdout (stdout is for MCP protocol)
var logFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".recall", "logs", $"{DateTime.Now:yyyy-MM-dd}.log");
Directory.CreateDirectory(Path.GetDirectoryName(logFilePath)!);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
        standardErrorFromLevel: LogEventLevel.Verbose) // Write ALL logs to stderr
    .WriteTo.File(logFilePath,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}",
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);

// Configure logging to use Serilog
builder.Logging.ClearProviders();
builder.Services.AddSerilog(Log.Logger);

// Register services as singletons
var recallDir = Path.Combine(Directory.GetCurrentDirectory(), ".recall");
var indexPath = Path.Combine(recallDir, "index.db");

builder.Services.AddSingleton<EmbeddingService>(sp => new EmbeddingService());
builder.Services.AddSingleton<JsonlStorageService>(sp => new JsonlStorageService(recallDir));
builder.Services.AddSingleton<VectorIndexService>(sp => new VectorIndexService(indexPath));
builder.Services.AddSingleton<FileWatcherService>(sp => new FileWatcherService(recallDir, sp));


// Configure MCP server with stdio transport and auto-discover tools
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new ModelContextProtocol.Protocol.Implementation
        {
            Name = "Recall",
            Version = "0.1.0"
        };
        options.ServerInstructions = Instructions.Get();
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Build and run
var host = builder.Build();

// Log startup message
Log.Information("🧠 Recall MCP Server started");
Log.Information("📁 Storage: .recall/ directory");
Log.Information("🔧 Tools: store, recall");
Log.Information("🔍 Search: 384-dim all-MiniLM-L6-v2 embeddings");
Log.Information("📝 Logs: {LogPath}", logFilePath);

try
{
    await host.RunAsync();
    return 0;
}
finally
{
    // Ensure all logs are flushed before shutdown
    await Log.CloseAndFlushAsync();
}
