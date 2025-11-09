using System.Reflection;

namespace Recall.Commands;

public class InitCommand
{
    public static async Task<int> ExecuteAsync(string[] args)
    {
        var targetDir = Directory.GetCurrentDirectory();
        var claudeDir = Path.Combine(targetDir, ".claude");

        // Parse options
        var force = args.Contains("--force");

        Console.WriteLine("🧠 Recall MCP Server - Project Initialization");
        Console.WriteLine();

        try
        {
            await InitializeCommandsAsync(claudeDir, force);

            Console.WriteLine();
            Console.WriteLine("✅ Initialization complete!");
            Console.WriteLine();
            Console.WriteLine("Next steps:");
            Console.WriteLine("  1. Configure Recall MCP server in your Claude Code settings");
            Console.WriteLine("  2. Use /recall and /checkpoint commands");
            Console.WriteLine("  3. Customize templates in .claude/ to fit your workflow");
            Console.WriteLine();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task InitializeCommandsAsync(string claudeDir, bool force)
    {
        var commandsDir = Path.Combine(claudeDir, "commands");
        Directory.CreateDirectory(commandsDir);

        Console.WriteLine("📝 Initializing slash commands...");

        var assembly = Assembly.GetExecutingAssembly();
        var templateNames = assembly.GetManifestResourceNames()
            .Where(n => n.Contains(".Templates.commands."))
            .ToList();

        foreach (var resourceName in templateNames)
        {
            var fileName = resourceName.Split('.').SkipWhile(p => p != "commands").Skip(1).First();
            var targetPath = Path.Combine(commandsDir, fileName + ".md");

            if (File.Exists(targetPath) && !force)
            {
                Console.WriteLine($"  ⏭️  Skipped {fileName}.md (already exists, use --force to overwrite)");
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) continue;

            using var reader = new StreamReader(stream);
            var content = await reader.ReadToEndAsync();

            await File.WriteAllTextAsync(targetPath, content);
            Console.WriteLine($"  ✅ Created {fileName}.md");
        }
    }

    public static void PrintUsage()
    {
        Console.WriteLine("Usage: recall-mcp init [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --force       Overwrite existing files");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  recall-mcp init         # Initialize slash commands");
        Console.WriteLine("  recall-mcp init --force # Reinitialize, overwriting existing files");
        Console.WriteLine();
        Console.WriteLine("Note: Hooks are configured in .claude/settings.json");
        Console.WriteLine("      See https://code.claude.com/docs/en/hooks-guide for examples");
    }
}
