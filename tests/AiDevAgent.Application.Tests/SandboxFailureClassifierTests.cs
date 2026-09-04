using AiDevAgent.Application.Sandbox;
using AiDevAgent.Infrastructure.Docker;

namespace AiDevAgent.Application.Tests;

public class SandboxFailureClassifierTests
{
    [Fact]
    public void DetectsNu1301RestoreFailure()
    {
        const string stderr = """
            error NU1301: Unable to load the service index for source https://api.nuget.org/v3/index.json.
            The HTTP request to 'GET https://api.nuget.org/v3/index.json' has failed with an error: The operation was canceled.
            A connection attempt failed because of a temporary network/resource-unavailable error.
            """;

        Assert.True(SandboxFailureClassifier.IsPackageFeedUnavailable(string.Empty, stderr));
    }

    [Fact]
    public void IgnoresOrdinaryCompileErrors()
    {
        const string stderr = "error CS1002: ; expected";
        Assert.False(SandboxFailureClassifier.IsPackageFeedUnavailable(stderr, stderr));
    }

    [Fact]
    public void PrefersCompilerErrorOverNuGetVulnerabilityWarnings()
    {
        const string log = """
            WeatherApi.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.6 has a known high severity vulnerability
            WeatherApi.csproj : warning NU1903: Package 'Microsoft.OpenApi' 2.3.6 has a known high severity vulnerability
            Program.cs(6,18): error CS0411: The type arguments for method 'AddExceptionHandler<T>(IServiceCollection)' cannot be inferred from the usage. Try specifying the type arguments explicitly.
            Program.cs(23,1): warning ASPDEPR002: 'WithOpenApi' is obsolete
            """;

        var errors = SandboxLogExcerpt.ExtractCompilerErrors(log, string.Empty);

        Assert.Single(errors);
        Assert.Contains("CS0411", errors[0], StringComparison.Ordinal);
        Assert.DoesNotContain(errors, e => e.Contains("NU1903", StringComparison.Ordinal));
        Assert.Contains("CS0411", SandboxLogExcerpt.Summary("Build failed in sandbox.", errors), StringComparison.Ordinal);
    }
}

public class DockerSandboxArgumentBuilderTests
{
    [Fact]
    public void DefaultsToBridgeSoRestoreCanReachNuget()
    {
        Assert.Equal("bridge", DockerSandboxArgumentBuilder.NormalizeNetwork(null));
        Assert.Equal("none", DockerSandboxArgumentBuilder.NormalizeNetwork("none"));
    }

    [Fact]
    public void IncludesConfiguredNetworkInDockerArgs()
    {
        var args = DockerSandboxArgumentBuilder.Build(
            "mcr.microsoft.com/dotnet/sdk:10.0",
            @"C:\ws",
            "dotnet build --nologo",
            "1g",
            "1",
            "bridge");

        Assert.Contains("--network bridge", args, StringComparison.Ordinal);
        Assert.DoesNotContain("--network none", args, StringComparison.Ordinal);
        Assert.Contains("dotnet build --nologo", args, StringComparison.Ordinal);
    }
}
