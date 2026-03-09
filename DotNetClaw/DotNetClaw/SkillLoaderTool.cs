using System.ComponentModel;
using System.Text.Json;

namespace DotNetClaw;

/// <summary>
/// Loads agent skills from the coffeeshop-cli tool.
/// Skills are SKILL.md manifests that define multi-step agentic workflows.
/// </summary>
public sealed class SkillLoaderTool(ExecTool execTool, ILogger<SkillLoaderTool> logger)
{
    /// <summary>
    /// Lists all available skills from coffeeshop-cli.
    /// Returns a JSON array of skill names.
    /// </summary>
    [Description("List all available agent skills from coffeeshop-cli. " +
                 "Skills are multi-step workflows that guide complex tasks. " +
                 "Use this to discover what skills are available, then call load_skill to activate one.")]
    public async Task<string> ListSkillsAsync(CancellationToken ct = default)
    {
        logger.LogInformation("[SkillLoaderTool] ListSkillsAsync called");

        var cwd = GetCoffeeshopCliDirectory();
        var projectPath = Path.Combine(cwd, "src/CoffeeshopCli/CoffeeshopCli.csproj");
        var command = $"dotnet run --project \"{projectPath}\" -- skills list --json";
        
        var result = await execTool.RunAsync(command, workingDirectory: cwd, ct: ct);
        
        // Parse ExecTool result
        var execResult = JsonSerializer.Deserialize<ExecToolResult>(result);
        if (execResult?.exit_code != 0)
        {
            logger.LogWarning("[SkillLoaderTool] ListSkillsAsync failed: {Stderr}", execResult?.stderr);
            return JsonSerializer.Serialize(new { error = "Failed to list skills", details = execResult?.stderr });
        }

        return execResult.stdout;
    }

    /// <summary>
    /// Loads a specific skill by name, returning the full SKILL.md manifest.
    /// The manifest contains step-by-step instructions for executing the skill.
    /// </summary>
    [Description("Load a specific agent skill by name. Returns the full SKILL.md manifest with step-by-step instructions. " +
                 "After loading, follow the instructions in the skill to complete the workflow. " +
                 "Example: load_skill('coffeeshop-counter-service') to help users order coffee.")]
    public async Task<string> LoadSkillAsync(
        [Description("The name of the skill to load (e.g., 'coffeeshop-counter-service')")]
        string skillName,
        CancellationToken ct = default)
    {
        logger.LogInformation("[SkillLoaderTool] LoadSkillAsync called: {SkillName}", skillName);

        var cwd = GetCoffeeshopCliDirectory();
        var projectPath = Path.Combine(cwd, "src/CoffeeshopCli/CoffeeshopCli.csproj");
        var command = $"dotnet run --project \"{projectPath}\" -- skills show {skillName} --json";
        
        var result = await execTool.RunAsync(command, workingDirectory: cwd, ct: ct);
        
        // Parse ExecTool result
        var execResult = JsonSerializer.Deserialize<ExecToolResult>(result);
        if (execResult?.exit_code != 0)
        {
            logger.LogWarning("[SkillLoaderTool] LoadSkillAsync failed: {SkillName}, stderr={Stderr}", 
                skillName, execResult?.stderr);
            return JsonSerializer.Serialize(new 
            { 
                error = $"Skill '{skillName}' not found", 
                details = execResult?.stderr 
            });
        }

        // Return the skill content - the agent will handle the JSON format directly
        // Note: There's a known issue with coffeeshop-cli YAML->JSON serialization
        // where multi-line strings contain literal newlines. We work around this by
        // just returning the content as-is for the agent to process.
        logger.LogInformation("[SkillLoaderTool] Skill '{SkillName}' loaded successfully ({Length} bytes)", 
            skillName, execResult.stdout.Length);
        
        return $"""
        Skill '{skillName}' loaded from coffeeshop-cli:
        
        {execResult.stdout}
        
        Extract the 'body' field and follow the instructions within it.
        """;
    }

    private static string GetCoffeeshopCliDirectory()
    {
        // Get DotNetClaw project directory (where this assembly runs from)
        var dotnetclawDir = AppContext.BaseDirectory;
        
        // Navigate to parent of DotNetClaw solution, then to coffeeshop-cli
        // From DotNetClaw/DotNetClaw/bin/Debug/net10.0/ go up 5 levels to parent experiment folder
        var parentDir = Path.GetFullPath(Path.Combine(dotnetclawDir, "../../../../.."));
        return Path.Combine(parentDir, "coffeeshop-cli");
    }

    // Internal types for JSON deserialization
    private record ExecToolResult(int exit_code, string stdout, string stderr, bool success);
    
    private record SkillShowResult(
        SkillFrontmatter? frontmatter,
        string body
    );

    private record SkillFrontmatter(
        string? name,
        string? description,
        SkillMetadata? metadata
    );

    private record SkillMetadata(
        string? version,
        string? author,
        string? category,
        string? loop_type
    );
}
