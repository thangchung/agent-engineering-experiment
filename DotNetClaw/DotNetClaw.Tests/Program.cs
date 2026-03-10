using DotNetClaw;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

Console.WriteLine("==============================================");
Console.WriteLine("  DotNetClaw Skill Integration Test");
Console.WriteLine("==============================================\n");

// Setup logging
var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var execLogger = loggerFactory.CreateLogger<ExecTool>();
var skillLogger = loggerFactory.CreateLogger<SkillLoaderTool>();

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        // Use an existing executable so SkillLoaderTool can be constructed in test environments.
        ["CoffeeshopCli:ExecutablePath"] = "/usr/bin/true"
    })
    .Build();

// Test 1: ExecTool blocks dangerous commands
Console.WriteLine("Test 1: ExecTool Safety - Blocking 'rm' command");
Console.WriteLine("-----------------------------------------------");
var execTool = new ExecTool(execLogger, configuration);
var blockedResult = await execTool.RunAsync("rm -rf /tmp/test");
Console.WriteLine($"Result: {(blockedResult.Contains("blocked") ? "✅ BLOCKED" : "❌ FAILED")}");
Console.WriteLine();

// Test 2: ExecTool allows safe commands
Console.WriteLine("Test 2: ExecTool Safety - Allowing 'echo' command");
Console.WriteLine("-----------------------------------------------");
var safeResult = await execTool.RunAsync("echo 'Hello from ExecTool'");
Console.WriteLine($"Result: {(safeResult.Contains("Hello from ExecTool") ? "✅ SUCCESS" : "❌ FAILED")}");
Console.WriteLine();

// Test 3: SkillLoaderTool lists skills
Console.WriteLine("Test 3: SkillLoaderTool - List Skills");
Console.WriteLine("-----------------------------------------------");
var skillLoader = new SkillLoaderTool(execTool, configuration, skillLogger);
var skillsJson = await skillLoader.ListSkillsAsync();
var hasSkills = skillsJson.Contains("coffeeshop-counter-service");
Console.WriteLine($"Skills found: {hasSkills}");
Console.WriteLine($"Result: {(hasSkills ? "✅ SUCCESS" : "❌ FAILED")}");
Console.WriteLine();

// Test 4: SkillLoaderTool loads a specific skill
Console.WriteLine("Test 4: SkillLoaderTool - Load Skill");
Console.WriteLine("-----------------------------------------------");
var manifest = await skillLoader.LoadSkillAsync("coffeeshop-counter-service");
var hasManifest = manifest.Contains("coffeeshop-counter-service") && 
                  manifest.Contains("Coffee Shop");
Console.WriteLine($"Manifest loaded: {hasManifest}");
Console.WriteLine($"Manifest preview (first 300 chars):");
Console.WriteLine(manifest.Substring(0, Math.Min(300, manifest.Length)) + "...");
Console.WriteLine($"\nResult: {(hasManifest ? "✅ SUCCESS" : "❌ FAILED")}");
Console.WriteLine();

Console.WriteLine("==============================================");
Console.WriteLine("  All Tests Completed!");
Console.WriteLine("==============================================");
