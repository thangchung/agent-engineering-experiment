using ContextEngineering.Core.Entities;
using ContextEngineering.Core.Interfaces;
using Microsoft.Extensions.AI;
using System.ComponentModel;

namespace ContextEngineering.Infrastructure.Plugins;

/// <summary>
/// AIFunction plugin for scratchpad operations.
/// Provides tools for the LLM to read and update user scratchpads.
/// Use AIFunctionFactory.Create() to wrap methods as AIFunction tools.
/// </summary>
public class ScratchpadPlugin(IScratchpadRepository scratchpadRepository)
{
    /// <summary>
    /// Read the user's scratchpad content including preferences and completed tasks.
    /// </summary>
    [Description("Read the user's scratchpad content including preferences and completed tasks.")]
    public async Task<string> ReadScratchpadAsync(
        [Description("The user ID to read scratchpad for")] string userId,
        CancellationToken ct = default)
    {
        var scratchpads = await scratchpadRepository.GetAllByUserIdAsync(userId, ct);

        if (scratchpads.Count == 0)
        {
            return "No scratchpad data found for this user.";
        }

        var result = scratchpads
            .Select(s => $"[{s.Category.ToUpperInvariant()}]\n{s.Content}")
            .Aggregate((a, b) => $"{a}\n\n{b}");

        return result;
    }

    /// <summary>
    /// Update the user's scratchpad with new preferences or completed tasks.
    /// </summary>
    [Description("Update the user's scratchpad with new preferences or completed tasks.")]
    public async Task<string> UpdateScratchpadAsync(
        [Description("The user ID to update scratchpad for")] string userId,
        [Description("Category: 'preferences' for user preferences, 'tasks' for completed tasks")] string category,
        [Description("The content to store in the scratchpad")] string content,
        CancellationToken ct = default)
    {
        if (category != ScratchpadCategory.Preferences && category != ScratchpadCategory.Tasks)
        {
            return $"Invalid category '{category}'. Use 'preferences' or 'tasks'.";
        }

        var scratchpad = await scratchpadRepository.UpsertAsync(userId, category, content, ct);
        return $"Scratchpad [{category}] updated successfully at {scratchpad.UpdatedAt:u}.";
    }

    /// <summary>
    /// Get all AIFunction tools from this plugin.
    /// </summary>
    public IEnumerable<AIFunction> GetTools()
    {
        yield return AIFunctionFactory.Create(
            ReadScratchpadAsync,
            name: "read_scratchpad",
            description: "Read the user's scratchpad content including preferences and completed tasks.");

        yield return AIFunctionFactory.Create(
            UpdateScratchpadAsync,
            name: "update_scratchpad",
            description: "Update the user's scratchpad with new preferences or completed tasks.");
    }
}
