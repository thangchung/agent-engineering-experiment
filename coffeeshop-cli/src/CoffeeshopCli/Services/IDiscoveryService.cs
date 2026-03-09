namespace CoffeeshopCli.Services;

/// <summary>
/// Service for discovering data models and agent skills.
/// </summary>
public interface IDiscoveryService
{
    /// <summary>
    /// Discover all available data models.
    /// </summary>
    IReadOnlyList<ModelInfo> DiscoverModels();

    /// <summary>
    /// Discover all available agent skills from filesystem.
    /// </summary>
    IReadOnlyList<SkillInfo> DiscoverSkills();
}

public record ModelInfo
{
    public required string Name { get; init; }
    public required Type Type { get; init; }
    public required int PropertyCount { get; init; }
}

public record SkillInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Version { get; init; }
    public required string Category { get; init; }
    public required string LoopType { get; init; }
    public required string Path { get; init; }
    public string? Content { get; init; }
}
