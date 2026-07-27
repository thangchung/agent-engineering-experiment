using AgenticTodo.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace AgenticTodo.Tests;

public class ServiceDefaultsSmokeTests
{
    [Fact]
    public void AddServiceDefaults_registers_health_checks_and_builds()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.AddServiceDefaults();

        using var host = builder.Build();

        // ServiceDefaults must register the health-check service (the "self" live check).
        var healthCheckService = host.Services.GetService<HealthCheckService>();
        Assert.NotNull(healthCheckService);
    }
}
