using System.Text.RegularExpressions;

namespace AiDevAgent.Application.Sandbox;

/// <summary>
/// Pulls compiler/test errors out of noisy MSBuild logs so agents are not
/// distracted by leading vulnerability warnings (NU1903, etc.).
/// </summary>
public static class SandboxLogExcerpt
{
    private static readonly Regex CompilerErrorLine = new(
        @":\s+error\s+(CS|MSB|CA)\d+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<string> ExtractCompilerErrors(string? stdout, string? stderr)
    {
        var text = Combine(stdout, stderr);
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (line.Contains(": warning ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!CompilerErrorLine.IsMatch(line)
                && !line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (seen.Add(line))
                errors.Add(line);
        }

        return errors;
    }

    public static IReadOnlyList<string> ExtractErrorFiles(string workspacePath, string? stdout, string? stderr)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        foreach (var line in ExtractCompilerErrors(stdout, stderr))
        {
            var idx = line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase);
            var raw = idx > 0 ? line[..idx] : line;
            try
            {
                var relative = Workspace.WorkspacePaths.ToRelative(workspacePath, raw).Replace('\\', '/');
                if (relative is "." or "")
                    continue;
                if (seen.Add(relative))
                    files.Add(relative);
            }
            catch (Exception)
            {
                // ignore unparseable log lines
            }
        }

        return files;
    }

    public static string ForAgent(string? stdout, string? stderr, int maxChars = 2_400)
    {
        var errors = ExtractCompilerErrors(stdout, stderr);
        if (errors.Count > 0)
            return Truncate(string.Join(Environment.NewLine, errors.Take(12)), maxChars);

        var combined = Combine(stdout, stderr);
        return Truncate(combined, maxChars);
    }

    public static string Summary(string fallback, IReadOnlyList<string> compilerErrors)
    {
        if (compilerErrors.Count == 0)
            return fallback;

        var first = compilerErrors[0];
        var code = Regex.Match(first, @"error\s+(CS|MSB|CA)\d+", RegexOptions.IgnoreCase);
        var prefix = code.Success ? $"Build failed ({code.Value.ToUpperInvariant()})" : "Build failed";
        var brief = Truncate(first, 220);
        return $"{prefix}: {brief}";
    }

    private static string Combine(string? stdout, string? stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return stdout ?? string.Empty;
        if (string.IsNullOrWhiteSpace(stdout))
            return stderr;
        return stdout + Environment.NewLine + stderr;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
            return value;
        return value[..max] + "…";
    }
}
