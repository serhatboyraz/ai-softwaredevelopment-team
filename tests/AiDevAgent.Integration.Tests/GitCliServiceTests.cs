using System.Diagnostics;
using AiDevAgent.Infrastructure.Git;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiDevAgent.Integration.Tests;

public class GitCliServiceTests
{
    [Fact]
    public async Task CheckoutTargetBranch_FallsBackToRemoteDefault_WhenRequestedBranchIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "aidevagent-git-" + Guid.NewGuid().ToString("N"));
        var seed = Path.Combine(root, "seed");
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(root);

        try
        {
            await Git(root, $"init -b main \"{seed}\"");
            await File.WriteAllTextAsync(Path.Combine(seed, "README.md"), "seed");
            await Git(seed, "add README.md");
            await Git(seed, "-c user.email=test@example.invalid -c user.name=test commit -m seed");

            await Git(root, $"clone \"{seed}\" \"{workspace}\"");

            var git = new GitCliService(NullLogger<GitCliService>.Instance);
            var checkedOut = await git.CheckoutTargetBranchAsync(workspace, "develop");

            Assert.Equal("main", checkedOut);
            var current = (await GitCapture(workspace, "branch --show-current")).Trim();
            Assert.Equal("main", current);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task GetRemoteDefaultBranch_ReturnsMain()
    {
        var root = Path.Combine(Path.GetTempPath(), "aidevagent-git-" + Guid.NewGuid().ToString("N"));
        var seed = Path.Combine(root, "seed");
        var workspace = Path.Combine(root, "workspace");
        Directory.CreateDirectory(root);

        try
        {
            await Git(root, $"init -b main \"{seed}\"");
            await File.WriteAllTextAsync(Path.Combine(seed, "README.md"), "seed");
            await Git(seed, "add README.md");
            await Git(seed, "-c user.email=test@example.invalid -c user.name=test commit -m seed");
            await Git(root, $"clone \"{seed}\" \"{workspace}\"");

            var git = new GitCliService(NullLogger<GitCliService>.Instance);
            var detected = await git.GetRemoteDefaultBranchAsync(workspace);

            Assert.Equal("main", detected);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static async Task Git(string workingDirectory, string args)
    {
        var (exit, _, stderr) = await Run(workingDirectory, args);
        if (exit != 0)
            throw new InvalidOperationException($"git {args} failed: {stderr}");
    }

    private static async Task<string> GitCapture(string workingDirectory, string args)
    {
        var (exit, stdout, stderr) = await Run(workingDirectory, args);
        if (exit != 0)
            throw new InvalidOperationException($"git {args} failed: {stderr}");
        return stdout;
    }

    private static async Task<(int Exit, string StdOut, string StdErr)> Run(string workingDirectory, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("git missing");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Windows can keep git pack files locked briefly.
        }
    }
}
