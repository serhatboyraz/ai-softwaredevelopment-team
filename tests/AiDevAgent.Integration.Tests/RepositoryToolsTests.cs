using AiDevAgent.Infrastructure.Repository;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiDevAgent.Integration.Tests;

public class RepositoryToolsTests
{
    [Fact]
    public async Task WriteFile_RejectsPathEscape()
    {
        var root = Path.Combine(Path.GetTempPath(), "aidevagent-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tools = new FileSystemRepositoryTools(NullLogger<FileSystemRepositoryTools>.Instance);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tools.WriteFileAsync(root, "../outside.txt", "nope"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAndRead_AcceptsDockerWorkspacePrefix()
    {
        var root = Path.Combine(Path.GetTempPath(), "aidevagent-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tools = new FileSystemRepositoryTools(NullLogger<FileSystemRepositoryTools>.Instance);
            await tools.WriteFileAsync(root, "/workspace/src/Hello.txt", "hello");
            var content = await tools.ReadFileAsync(root, "src/Hello.txt");
            Assert.Equal("hello", content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAndRead_AcceptsAbsolutePathInsideWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "aidevagent-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tools = new FileSystemRepositoryTools(NullLogger<FileSystemRepositoryTools>.Instance);
            var absolute = Path.Combine(root, "src", "Hello.txt");
            await tools.WriteFileAsync(root, absolute, "hello");
            var content = await tools.ReadFileAsync(root, "src/Hello.txt");
            Assert.Equal("hello", content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAndRead_RoundTrips()
    {
        var root = Path.Combine(Path.GetTempPath(), "aidevagent-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var tools = new FileSystemRepositoryTools(NullLogger<FileSystemRepositoryTools>.Instance);
            await tools.WriteFileAsync(root, "src/Hello.txt", "hello");
            var content = await tools.ReadFileAsync(root, "src/Hello.txt");
            Assert.Equal("hello", content);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
