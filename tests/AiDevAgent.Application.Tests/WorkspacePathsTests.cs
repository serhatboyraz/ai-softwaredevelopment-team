using AiDevAgent.Application.Sandbox;
using AiDevAgent.Application.Workspace;

namespace AiDevAgent.Application.Tests;

public class WorkspacePathsTests
{
    [Fact]
    public void RelativePath_StaysRelative()
    {
        var ws = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]));
        var relative = WorkspacePaths.ToRelative(ws, "src/WeatherApi/Program.cs");
        Assert.Equal(Path.Combine("src", "WeatherApi", "Program.cs"), relative);
    }

    [Fact]
    public void DockerWorkspacePrefix_BecomesRelative()
    {
        var ws = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]));
        var relative = WorkspacePaths.ToRelative(ws, "/workspace/src/WeatherApi/Program.cs");
        Assert.Equal(Path.Combine("src", "WeatherApi", "Program.cs"), relative);
    }

    [Fact]
    public void HostAbsolutePathInsideWorkspace_BecomesRelative()
    {
        var ws = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]));
        var absolute = Path.Combine(ws, "src", "WeatherApi", "Program.cs");
        var relative = WorkspacePaths.ToRelative(ws, absolute + "(6,18)");
        Assert.Equal(Path.Combine("src", "WeatherApi", "Program.cs"), relative);
    }

    [Fact]
    public void LeadingSlashRelative_DoesNotEscapeOnWindows()
    {
        var ws = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(ws);
        try
        {
            var full = WorkspacePaths.ResolveInside(ws, "/src/WeatherApi/Program.cs");
            Assert.True(WorkspacePaths.IsInside(ws, full));
            Assert.Contains("WeatherApi", full, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(ws, recursive: true);
        }
    }

    [Fact]
    public void ParentTraversal_Throws()
    {
        var ws = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]));
        Assert.Throws<InvalidOperationException>(() => WorkspacePaths.ResolveInside(ws, "../outside.txt"));
    }

    [Fact]
    public void ExtractErrorFiles_UsesWorkspaceRelativePath()
    {
        var ws = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws-" + Guid.NewGuid().ToString("N")[..8]));
        var log = $"""
            warning NU1903: Package 'Microsoft.OpenApi' 2.3.6 has a known high severity vulnerability
            {Path.Combine(ws, "src", "WeatherApi", "Program.cs")}(6,18): error CS0411: type arguments cannot be inferred
            """;

        var files = SandboxLogExcerpt.ExtractErrorFiles(ws, log, string.Empty);
        Assert.Contains(files, f => f.Replace('\\', '/') == "src/WeatherApi/Program.cs");
    }
}
