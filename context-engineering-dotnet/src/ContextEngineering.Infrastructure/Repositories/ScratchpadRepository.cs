using ContextEngineering.Core.Entities;
using ContextEngineering.Core.Interfaces;
using ContextEngineering.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContextEngineering.Infrastructure.Repositories;

public class ScratchpadRepository(AppDbContext dbContext) : IScratchpadRepository
{
    public async Task<Scratchpad?> GetByUserIdAndCategoryAsync(string userId, string category, CancellationToken ct = default)
    {
        return await dbContext.Scratchpads
            .FirstOrDefaultAsync(s => s.UserId == userId && s.Category == category, ct);
    }

    public async Task<IReadOnlyList<Scratchpad>> GetAllByUserIdAsync(string userId, CancellationToken ct = default)
    {
        return await dbContext.Scratchpads
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.Category)
            .ToListAsync(ct);
    }

    public async Task<Scratchpad> UpsertAsync(string userId, string category, string content, CancellationToken ct = default)
    {
        var existing = await GetByUserIdAndCategoryAsync(userId, category, ct);
        
        if (existing is not null)
        {
            existing.Content = content;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new Scratchpad
            {
                UserId = userId,
                Category = category,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            dbContext.Scratchpads.Add(existing);
        }
        
        await dbContext.SaveChangesAsync(ct);
        return existing;
    }

    public async Task DeleteAsync(string userId, string category, CancellationToken ct = default)
    {
        await dbContext.Scratchpads
            .Where(s => s.UserId == userId && s.Category == category)
            .ExecuteDeleteAsync(ct);
    }
}
