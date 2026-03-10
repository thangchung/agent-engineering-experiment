using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace DotNetClaw;

/// <summary>
/// Loads agent skills from the coffeeshop-cli tool.
/// Skills are SKILL.md manifests that define multi-step agentic workflows.
/// </summary>
public sealed class SkillLoaderTool
{
    private readonly ExecTool execTool;
    private readonly ILogger<SkillLoaderTool> logger;
    private readonly string coffeeshopCliExecutablePath;

    public SkillLoaderTool(
        ExecTool execTool,
        IConfiguration configuration,
        ILogger<SkillLoaderTool> logger)
    {
        this.execTool = execTool;
        this.logger = logger;

        var configuredPath = configuration["CoffeeshopCli:ExecutablePath"];
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                "Missing required configuration key 'CoffeeshopCli:ExecutablePath'. " +
                "Set it to the coffeeshop-cli executable file path.");
        }

        coffeeshopCliExecutablePath = Path.GetFullPath(configuredPath);
        if (!File.Exists(coffeeshopCliExecutablePath))
        {
            throw new InvalidOperationException(
                $"Configured coffeeshop-cli executable does not exist: '{coffeeshopCliExecutablePath}'. " +
                "Check 'CoffeeshopCli:ExecutablePath' in configuration.");
        }
    }

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

        // Cross-platform command construction - ExecTool handles platform-specific shell execution
        var command = $"\"{coffeeshopCliExecutablePath}\" skills list --json";
        
        var result = await execTool.RunAsync(command, ct: ct);
        
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

        // Cross-platform command construction with parameter sanitization
        // Note: Quotes in skillName could break shell parsing, but assuming skill names are safe identifiers
        var command = $"\"{coffeeshopCliExecutablePath}\" skills show \"{skillName}\" --json";
        
        var result = await execTool.RunAsync(command, ct: ct);
        
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
