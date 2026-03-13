using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DotNetClaw;

/// <summary>
/// Loads agent skills from the coffeeshop-cli tool.
/// Skills are SKILL.md manifests that define multi-step agentic workflows.
/// </summary>
public sealed class SkillLoaderTool
{
    private readonly ExecTool execTool;
    private readonly ILogger<SkillLoaderTool> logger;
    private readonly SkillLoaderMode mode;
    private readonly string? coffeeshopCliExecutablePath;
    private readonly string mcpBaseUrl;
    private readonly string mcpEndpointPath;
    private readonly int mcpTimeoutSeconds;

    public SkillLoaderTool(
        ExecTool execTool,
        IConfiguration configuration,
        ILogger<SkillLoaderTool> logger)
    {
        this.execTool = execTool;
        this.logger = logger;

        mode = ParseMode(configuration["CoffeeshopCli:Mode"]);

        mcpBaseUrl = configuration["CoffeeshopCli:Mcp:BaseUrl"] ?? "http://127.0.0.1:8080";
        mcpEndpointPath = ResolveMcpEndpointPath(
            configuration["CoffeeshopCli:Mcp:EndpointPath"],
            configuration["CoffeeshopCli:Mcp:SsePath"]);
        mcpTimeoutSeconds = ParsePositiveInt(configuration["CoffeeshopCli:Mcp:RequestTimeoutSeconds"], 20);

        var configuredPath = configuration["CoffeeshopCli:ExecutablePath"];
        string? resolvedCliPath = null;

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var fullPath = Path.GetFullPath(configuredPath);
            if (File.Exists(fullPath))
            {
                resolvedCliPath = fullPath;
            }
            else if (mode == SkillLoaderMode.Cli)
            {
                throw new InvalidOperationException(
                    $"Configured coffeeshop-cli executable does not exist: '{fullPath}'. " +
                    "Check 'CoffeeshopCli:ExecutablePath' in configuration.");
            }
        }

        if (mode == SkillLoaderMode.Cli)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException(
                    "Missing required configuration key 'CoffeeshopCli:ExecutablePath'. " +
                    "Set it to the coffeeshop-cli executable file path.");
            }

            if (string.IsNullOrWhiteSpace(resolvedCliPath))
            {
                throw new InvalidOperationException(
                    $"Configured coffeeshop-cli executable does not exist: '{Path.GetFullPath(configuredPath)}'. " +
                    "Check 'CoffeeshopCli:ExecutablePath' in configuration.");
            }
        }

        coffeeshopCliExecutablePath = resolvedCliPath;

        logger.LogInformation(
            "[SkillLoaderTool] Mode={Mode}, McpBaseUrl={McpBaseUrl}, McpEndpointPath={McpEndpointPath}",
            mode,
            mcpBaseUrl,
            mcpEndpointPath);
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
        logger.LogInformation("[SkillLoaderTool] ListSkillsAsync called (Mode={Mode})", mode);

        if (mode == SkillLoaderMode.Mcp)
            return await ListSkillsViaMcpAsync(ct);

        return await ListSkillsViaCliAsync(ct);
    }

    private async Task<string> ListSkillsViaCliAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coffeeshopCliExecutablePath))
            return JsonSerializer.Serialize(new { error = "CLI mode is not configured" });

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
        logger.LogInformation("[SkillLoaderTool] LoadSkillAsync called: {SkillName} (Mode={Mode})", skillName, mode);

        if (mode == SkillLoaderMode.Mcp)
            return await LoadSkillViaMcpAsync(skillName, ct);

        return await LoadSkillViaCliAsync(skillName, ct);
    }

    /// <summary>
    /// Lists menu items from the coffeeshop MCP bridge.
    /// </summary>
    [Description("List coffee shop menu items from MCP. Use this before building an order.")]
    public async Task<string> ListMenuItemsAsync(CancellationToken ct = default)
    {
        logger.LogInformation("[SkillLoaderTool] ListMenuItemsAsync called (Mode={Mode})", mode);

        var callResult = await CallMcpToolAsync("menu_list_items", new { }, ct);
        if (!callResult.ok)
            return JsonSerializer.Serialize(new { error = "Failed to list menu items via MCP", details = callResult.error });

        return callResult.text ?? JsonSerializer.Serialize(new { error = "MCP returned empty response" });
    }

    /// <summary>
    /// Looks up a customer by email or customer id through MCP.
    /// </summary>
    [Description("Lookup a customer by email or customer_id via coffeeshop MCP bridge.")]
    public async Task<string> LookupCustomerAsync(
        [Description("Customer email to look up. Prefer this when available.")]
        string? email = null,
        [Description("Customer id to look up, used when email is not provided.")]
        string? customerId = null,
        CancellationToken ct = default)
    {
        logger.LogInformation("[SkillLoaderTool] LookupCustomerAsync called: email={Email}, customerId={CustomerId}", email, customerId);

        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(customerId))
            return JsonSerializer.Serialize(new { error = "Provide email or customerId" });

        var callResult = await CallMcpToolAsync(
            "customer_lookup",
            new { email, customer_id = customerId },
            ct);

        if (!callResult.ok)
            return JsonSerializer.Serialize(new { error = "Failed to lookup customer via MCP", details = callResult.error });

        return callResult.text ?? JsonSerializer.Serialize(new { error = "MCP returned empty response" });
    }

    /// <summary>
    /// Submits an order through MCP.
    /// </summary>
    [Description("Submit an order through coffeeshop MCP bridge. itemsJson must be a JSON array like [{\"item_type\":\"Latte\",\"qty\":1}].")]
    public async Task<string> SubmitOrderAsync(
        [Description("Customer id for the order")]
        string customerId,
        [Description("JSON array of order lines. Example: [{\"item_type\":\"Latte\",\"qty\":1}]")]
        string itemsJson,
        CancellationToken ct = default)
    {
        logger.LogInformation("[SkillLoaderTool] SubmitOrderAsync called: customerId={CustomerId}", customerId);

        if (string.IsNullOrWhiteSpace(customerId))
            return JsonSerializer.Serialize(new { error = "customerId is required" });

        List<OrderLineInput>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<OrderLineInput>>(itemsJson);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = "itemsJson is invalid", details = ex.Message });
        }

        if (items is null || items.Count == 0)
            return JsonSerializer.Serialize(new { error = "itemsJson must contain at least one item" });

        var callResult = await CallMcpToolAsync(
            "order_submit",
            new { customer_id = customerId, items },
            ct);

        if (!callResult.ok)
            return JsonSerializer.Serialize(new { error = "Failed to submit order via MCP", details = callResult.error });

        return callResult.text ?? JsonSerializer.Serialize(new { error = "MCP returned empty response" });
    }

    private async Task<string> LoadSkillViaCliAsync(string skillName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(coffeeshopCliExecutablePath))
            return JsonSerializer.Serialize(new { error = "CLI mode is not configured" });

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

    private async Task<string> ListSkillsViaMcpAsync(CancellationToken ct)
    {
        var callResult = await CallMcpToolAsync("skill_list", new { }, ct);
        if (!callResult.ok)
        {
            if (!string.IsNullOrWhiteSpace(coffeeshopCliExecutablePath))
            {
                logger.LogWarning("[SkillLoaderTool] MCP list failed; falling back to CLI: {Error}", callResult.error);
                return await ListSkillsViaCliAsync(ct);
            }

            return JsonSerializer.Serialize(new { error = "Failed to list skills via MCP", details = callResult.error });
        }

        return callResult.text ?? JsonSerializer.Serialize(new { error = "MCP returned empty response" });
    }

    private async Task<string> LoadSkillViaMcpAsync(string skillName, CancellationToken ct)
    {
        var callResult = await CallMcpToolAsync("skill_show", new { name = skillName }, ct);
        if (!callResult.ok)
        {
            if (!string.IsNullOrWhiteSpace(coffeeshopCliExecutablePath))
            {
                logger.LogWarning("[SkillLoaderTool] MCP load failed for {SkillName}; falling back to CLI: {Error}",
                    skillName,
                    callResult.error);
                return await LoadSkillViaCliAsync(skillName, ct);
            }

            return JsonSerializer.Serialize(new
            {
                error = $"Skill '{skillName}' not found via MCP",
                details = callResult.error
            });
        }

        var responseText = callResult.text ?? JsonSerializer.Serialize(new { ok = false, error = "empty_mcp_response" });

        return $"""
        Skill '{skillName}' loaded from coffeeshop MCP bridge:

        {responseText}

        Extract the 'body' field and follow the instructions within it.
        """;
    }

    private async Task<(bool ok, string? text, string? error)> CallMcpToolAsync(
        string toolName,
        object arguments,
        CancellationToken ct)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(mcpTimeoutSeconds));

            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(BuildAbsoluteUrl(mcpEndpointPath)),
                TransportMode = HttpTransportMode.AutoDetect,
                ConnectionTimeout = TimeSpan.FromSeconds(mcpTimeoutSeconds)
            });

            await using var mcpClient = await McpClient.CreateAsync(
                transport,
                cancellationToken: linkedCts.Token);

            var callResponse = await mcpClient.CallToolAsync(
                toolName,
                CreateMcpArguments(arguments),
                cancellationToken: linkedCts.Token);

            if (callResponse.IsError == true)
                return (false, null, $"mcp_tool_call_failed: {toolName}");

            if (TryExtractMcpText(callResponse, out var text))
                return (true, text, null);

            return (false, null, "mcp_tool_response_missing_text");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, null, $"mcp_timeout_after_{mcpTimeoutSeconds}s");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[SkillLoaderTool] MCP call failed: tool={ToolName}", toolName);
            return (false, null, ex.Message);
        }
    }

    private static Dictionary<string, object?> CreateMcpArguments(object arguments)
    {
        var element = JsonSerializer.SerializeToElement(arguments);
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (element.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var property in element.EnumerateObject())
        {
            map[property.Name] = property.Value.Clone();
        }

        return map;
    }

    private static bool TryExtractMcpText(CallToolResult response, out string? text)
    {
        text = null;

        foreach (var item in response.Content)
        {
            if (item is TextContentBlock textContent && !string.IsNullOrWhiteSpace(textContent.Text))
            {
                text = textContent.Text;
                return true;
            }
        }

        return false;
    }

    private static string ResolveMcpEndpointPath(string? endpointPath, string? legacySsePath)
    {
        if (!string.IsNullOrWhiteSpace(endpointPath))
            return endpointPath;

        if (string.IsNullOrWhiteSpace(legacySsePath))
            return "/mcp";

        var trimmed = legacySsePath.Trim();
        if (trimmed.EndsWith("/sse", StringComparison.OrdinalIgnoreCase))
            return trimmed[..^4];

        return trimmed;
    }

    private string BuildAbsoluteUrl(string pathOrUrl)
    {
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
             absolute.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return absolute.ToString();
        }

        return $"{mcpBaseUrl.TrimEnd('/')}/{pathOrUrl.TrimStart('/')}";
    }

    private static SkillLoaderMode ParseMode(string? raw)
    {
        if (string.Equals(raw, "mcp", StringComparison.OrdinalIgnoreCase))
            return SkillLoaderMode.Mcp;

        return SkillLoaderMode.Cli;
    }

    private static int ParsePositiveInt(string? raw, int fallback)
    {
        if (int.TryParse(raw, out var value) && value > 0)
            return value;

        return fallback;
    }

    private enum SkillLoaderMode
    {
        Cli,
        Mcp
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

    private sealed record OrderLineInput(string item_type, int qty);
}
