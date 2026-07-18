using LoopRuntime.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LoopRuntime.Tests;

public class ServiceDefaultsTests
{
    [Fact]
    public void AddServiceDefaults_registers_open_telemetry_services()
    {
        var builder = new HostApplicationBuilder();

        builder.AddServiceDefaults();

        Assert.Contains(builder.Services, descriptor =>
            descriptor.ServiceType.FullName?.Contains("OpenTelemetry", StringComparison.Ordinal) == true);
    }
}
