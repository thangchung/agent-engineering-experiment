using CoffeeshopCli.Services;
using Xunit;

namespace CoffeeshopCli.Tests.Commands;

public sealed class CommandIntegrationTests
{
    [Fact]
    public void DiscoveryService_FindsExpectedModelsAndSkills()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        var skillsDir = Path.Combine(repoRoot, "skills");
        var discovery = new FileSystemDiscoveryService(new ModelRegistry(), skillsDir);

        var models = discovery.DiscoverModels();
        var skills = discovery.DiscoverSkills();

        Assert.Contains(models, m => m.Name == "Customer");
        Assert.Contains(models, m => m.Name == "Order");
        Assert.Contains(skills, s => s.Name == "coffeeshop-counter-service");
    }
}
