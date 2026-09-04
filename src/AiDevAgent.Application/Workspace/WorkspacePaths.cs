namespace AiDevAgent.Application.Workspace;

/// <summary>
/// Resolves agent/sandbox paths into a file inside the workspace.
/// <c>Path.Combine(workspace, absolutePath)</c> on Windows discards the workspace
/// when the model copies a Docker <c>/workspace/...</c> or host-absolute error path.
/// </summary>
public static class WorkspacePaths
{
    public static string ResolveInside(string workspacePath, string requestedPath)
    {
        var workspaceFull = Path.GetFullPath(workspacePath);
        var relative = ToRelative(workspaceFull, requestedPath);
        var full = Path.GetFullPath(Path.Combine(workspaceFull, relative));
        if (!IsInside(workspaceFull, full))
            throw new InvalidOperationException("Path escapes workspace.");
        return full;
    }

    public static string ToRelative(string workspacePath, string requestedPath)
    {
        if (string.IsNullOrWhiteSpace(requestedPath))
            throw new InvalidOperationException("Path is empty.");

        var workspaceFull = Path.GetFullPath(workspacePath);
        var cleaned = StripLocationSuffix(requestedPath.Trim()).Replace('\\', '/');

        if (cleaned.StartsWith("/workspace/", StringComparison.OrdinalIgnoreCase))
            return NormalizeRelative(cleaned["/workspace/".Length..]);
        if (cleaned.Equals("/workspace", StringComparison.OrdinalIgnoreCase))
            return ".";

        if (IsAbsolute(cleaned))
        {
            try
            {
                var absolute = Path.GetFullPath(ToNative(cleaned));
                if (IsInside(workspaceFull, absolute))
                    return Path.GetRelativePath(workspaceFull, absolute);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // fall through and treat as a leading-slash relative path
            }

            return NormalizeRelative(TrimDriveAndRoot(cleaned));
        }

        return NormalizeRelative(cleaned);
    }

    public static bool IsInside(string workspacePath, string fullPath)
    {
        var root = Path.GetFullPath(workspacePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(fullPath);
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                candidate.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAbsolute(string unixStyle)
        => Path.IsPathRooted(unixStyle) || Path.IsPathRooted(ToNative(unixStyle));

    private static string ToNative(string unixStyle)
        => unixStyle.Replace('/', Path.DirectorySeparatorChar);

    private static string TrimDriveAndRoot(string unixStyle)
    {
        if (unixStyle.Length >= 2 && char.IsLetter(unixStyle[0]) && unixStyle[1] == ':')
            unixStyle = unixStyle[2..];
        return unixStyle.TrimStart('/');
    }

    private static string NormalizeRelative(string unixStyle)
    {
        var s = unixStyle.TrimStart('/');
        if (s.StartsWith("./", StringComparison.Ordinal))
            s = s[2..];
        return string.IsNullOrEmpty(s) ? "." : ToNative(s);
    }

    private static string StripLocationSuffix(string path)
    {
        var idx = path.IndexOf('(');
        if (idx <= 0 || idx >= path.Length - 1 || !char.IsDigit(path[idx + 1]))
            return path;
        return path[..idx];
    }
}
