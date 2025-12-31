using ContextEngineering.Core.Entities;

namespace ContextEngineering.Core.Interfaces;

public interface IScratchpadRepository
{
    Task<Scratchpad?> GetByUserIdAndCategoryAsync(string userId, string category, CancellationToken ct = default);
    Task<IReadOnlyList<Scratchpad>> GetAllByUserIdAsync(string userId, CancellationToken ct = default);
    Task<Scratchpad> UpsertAsync(string userId, string category, string content, CancellationToken ct = default);
    Task DeleteAsync(string userId, string category, CancellationToken ct = default);
}
