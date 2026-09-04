using System.Diagnostics;
using AiDevAgent.Application.Sandbox;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Infrastructure.Docker;

/// <summary>
/// MVP sandbox: runs approved commands in an ephemeral docker container with the workspace mounted.
/// Falls back to a local process only when Sandbox:AllowLocalFallback=true (dev only).
/// Package restore needs feed access; use Docker:Network=bridge (default) or none for offline runs.
/// </summary>
public sealed class DockerSandboxExecutor(
    IConfiguration configuration,
    ILogger<DockerSandboxExecutor> logger) : ISandboxExecutor
{
    public async Task<SandboxResult> RunAsync(
        string workspacePath,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var image = configuration["Docker:SandboxImage"] ?? "mcr.microsoft.com/dotnet/sdk:10.0";
        var memory = configuration["Docker:Memory"] ?? "1g";
        var cpus = configuration["Docker:Cpus"] ?? "1";
        var network = DockerSandboxArgumentBuilder.NormalizeNetwork(configuration["Docker:Network"]);
        var allowLocal = configuration.GetValue("Sandbox:AllowLocalFallback", false);

        var fullPath = Path.GetFullPath(workspacePath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException(fullPath);

        var dockerArgs = DockerSandboxArgumentBuilder.Build(image, fullPath, command, memory, cpus, network);

        try
        {
            var dockerResult = await ExecuteAsync("docker", dockerArgs, timeout, cancellationToken);
            if (dockerResult.Success || !allowLocal || !ShouldFallBackLocally(dockerResult))
                return dockerResult;

            logger.LogWarning(
                "Docker sandbox failed ({Error}); using local fallback (dev only).",
                TruncateLog(string.IsNullOrWhiteSpace(dockerResult.StdErr) ? dockerResult.StdOut : dockerResult.StdErr));
            return await ExecuteLocalAsync(command, timeout, cancellationToken, fullPath);
        }
        catch (Exception ex) when (allowLocal)
        {
            logger.LogWarning(ex, "Docker sandbox unavailable; using local fallback (dev only).");
            return await ExecuteLocalAsync(command, timeout, cancellationToken, fullPath);
        }
    }

    private Task<SandboxResult> ExecuteLocalAsync(
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string fullPath)
        => ExecuteAsync(
            OperatingSystem.IsWindows() ? "cmd.exe" : "sh",
            OperatingSystem.IsWindows() ? $"/c {command}" : $"-lc {command}",
            timeout,
            cancellationToken,
            workingDirectory: fullPath);

    private static bool ShouldFallBackLocally(SandboxResult result)
        => LooksLikeDockerUnavailable(result.StdErr)
           || SandboxFailureClassifier.IsPackageFeedUnavailable(result.StdOut, result.StdErr);

    private static bool LooksLikeDockerUnavailable(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return false;

        ReadOnlySpan<string> markers =
        [
            "Cannot connect to the Docker daemon",
            "error during connect",
            "open //./pipe/docker",
            "dockerDesktopLinuxEngine",
            "Is the docker daemon running",
            "The system cannot find the file specified",
            "docker: command not found",
            "failed to connect"
        ];

        foreach (var marker in markers)
        {
            if (stderr.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string TruncateLog(string value)
        => value.Length <= 300 ? value : value[..300] + "…";

    private async Task<SandboxResult> ExecuteAsync(
        string fileName,
        string args,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (workingDirectory is not null)
            psi.WorkingDirectory = workingDirectory;

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var stdoutTask = TruncateAsync(process.StandardOutput.ReadToEndAsync(cts.Token));
            var stderrTask = TruncateAsync(process.StandardError.ReadToEndAsync(cts.Token));
            await process.WaitForExitAsync(cts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            sw.Stop();
            return new SandboxResult(process.ExitCode == 0, process.ExitCode, stdout, stderr, sw.Elapsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
            sw.Stop();
            return new SandboxResult(false, -1, string.Empty, "Sandbox execution timed out.", sw.Elapsed);
        }
    }

    private static async Task<string> TruncateAsync(Task<string> read)
    {
        var text = await read;
        const int max = 200_000;
        return text.Length <= max ? text : text[..max] + "\n…[truncated]";
    }
}
