using ContextEngineering.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace ContextEngineering.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Scratchpad> Scratchpads => Set<Scratchpad>();
    public DbSet<ConversationThread> ConversationThreads => Set<ConversationThread>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<TokenUsage> TokenUsages => Set<TokenUsage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        ConfigureScratchpad(modelBuilder);
        ConfigureConversationThread(modelBuilder);
        ConfigureChatMessage(modelBuilder);
        ConfigureTokenUsage(modelBuilder);
        SeedData(modelBuilder);
    }

    private static void ConfigureScratchpad(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Scratchpad>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.Category }).IsUnique();
            entity.Property(e => e.UserId).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Category).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Content).HasMaxLength(10000);
        });
    }

    private static void ConfigureConversationThread(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConversationThread>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.UserId).HasMaxLength(100).IsRequired();
            entity.HasMany(e => e.Messages)
                  .WithOne(m => m.ConversationThread)
                  .HasForeignKey(m => m.ConversationThreadId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureChatMessage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConversationThreadId);
            entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Content).IsRequired();
        });
    }

    private static void ConfigureTokenUsage(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TokenUsage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConversationThreadId);
            entity.Ignore(e => e.TotalTokens); // Computed property
            entity.HasOne(e => e.ConversationThread)
                  .WithMany()
                  .HasForeignKey(e => e.ConversationThreadId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        var userId = "demo-user";
        var now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Seed scratchpad data
        modelBuilder.Entity<Scratchpad>().HasData(
            new Scratchpad
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                UserId = userId,
                Category = ScratchpadCategory.Preferences,
                Content = "User prefers concise responses. Interested in .NET development and AI topics.",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Scratchpad
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                UserId = userId,
                Category = ScratchpadCategory.Tasks,
                Content = "Completed: Setup project structure, Added entity models",
                CreatedAt = now,
                UpdatedAt = now
            }
        );

        // Seed a conversation thread
        var threadId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        modelBuilder.Entity<ConversationThread>().HasData(
            new ConversationThread
            {
                Id = threadId,
                UserId = userId,
                CreatedAt = now,
                MessageCount = 2,
                TotalTokensUsed = 150,
                ReductionEventCount = 0
            }
        );

        // Seed chat messages
        modelBuilder.Entity<ChatMessage>().HasData(
            new ChatMessage
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                ConversationThreadId = threadId,
                Role = "user",
                Content = "Hello! I'm learning about context engineering in .NET.",
                TokenCount = 50,
                CreatedAt = now,
                IsSummary = false
            },
            new ChatMessage
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                ConversationThreadId = threadId,
                Role = "assistant",
                Content = "Welcome! Context engineering helps manage conversation context efficiently. I can help you understand techniques like chat history reduction and scratchpad patterns.",
                TokenCount = 100,
                CreatedAt = now.AddSeconds(1),
                IsSummary = false
            }
        );
    }
}
