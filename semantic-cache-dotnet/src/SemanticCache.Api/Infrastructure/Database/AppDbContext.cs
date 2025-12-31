using Microsoft.EntityFrameworkCore;
using SemanticCache.Api.Infrastructure.Database.Entities;

namespace SemanticCache.Api.Infrastructure.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<ConversationEntry> ConversationEntries => Set<ConversationEntry>();
    public DbSet<ContextMemory> ContextMemories => Set<ContextMemory>();
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();
    public DbSet<MetricsLog> MetricsLogs => Set<MetricsLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // UserProfile configuration
        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.HasIndex(e => e.Name);
            
            entity.HasMany(e => e.Preferences)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.ConversationEntries)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.ContextMemories)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        
        // UserPreference configuration
        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PreferenceKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.PreferenceValue).HasMaxLength(500).IsRequired();
            entity.HasIndex(e => new { e.UserId, e.PreferenceKey });
        });
        
        // ConversationEntry configuration
        modelBuilder.Entity<ConversationEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Role).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.HasIndex(e => new { e.UserId, e.ThreadId, e.Timestamp });
        });
        
        // ContextMemory configuration
        modelBuilder.Entity<ContextMemory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MemoryKey).HasMaxLength(100).IsRequired();
            entity.Property(e => e.MemoryValue).HasMaxLength(1000).IsRequired();
            entity.HasIndex(e => new { e.UserId, e.MemoryKey });
        });
        
        // CacheEntry configuration
        modelBuilder.Entity<CacheEntry>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Query).HasMaxLength(1000).IsRequired();
            entity.Property(e => e.Response).IsRequired();
            entity.Property(e => e.Embedding).IsRequired();
            entity.HasIndex(e => e.Query);
            entity.HasIndex(e => e.CreatedAt);
        });
        
        // MetricsLog configuration
        modelBuilder.Entity<MetricsLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Endpoint).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Method).HasMaxLength(10).IsRequired();
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => new { e.Endpoint, e.Timestamp });
        });
    }
}
