namespace AiDevAgent.Infrastructure.Docker;

public static class DockerSandboxArgumentBuilder
{
    public static string NormalizeNetwork(string? network)
    {
        if (string.Equals(network, "none", StringComparison.OrdinalIgnoreCase))
            return "none";
        if (string.Equals(network, "host", StringComparison.OrdinalIgnoreCase))
            return "host";
        return "bridge";
    }

    public static string Build(
        string image,
        string workspacePath,
        string command,
        string memory,
        string cpus,
        string network)
    {
        var escapedCommand = command.Replace("\"", "\\\"", StringComparison.Ordinal);
        return $"run --rm --network {network} --memory {memory} --cpus {cpus} " +
               "-e DOTNET_CLI_TELEMETRY_OPTOUT=1 -e NUGET_CERT_REVOCATION_MODE=offline " +
               $"-v \"{workspacePath}:/workspace:rw\" -w /workspace {image} " +
               $"sh -lc \"{escapedCommand}\"";
    }
}
