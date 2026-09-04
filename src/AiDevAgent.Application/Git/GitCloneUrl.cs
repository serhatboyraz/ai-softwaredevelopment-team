namespace AiDevAgent.Application.Git;

public static class GitBranches
{
    public const string FallbackDefault = "main";

    public static string OrFallback(string? branch)
        => string.IsNullOrWhiteSpace(branch) ? FallbackDefault : branch.Trim();
}

public static class GitCloneUrl
{
    public static string WithOptionalToken(string repositoryUrl, string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return repositoryUrl;
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
            return repositoryUrl;
        if (uri.Scheme is not ("http" or "https"))
            return repositoryUrl;

        var builder = new UriBuilder(uri)
        {
            UserName = "oauth2",
            Password = token
        };
        return builder.Uri.AbsoluteUri;
    }

    /// <summary>GitLab API project path (group/repo) from an https clone URL.</summary>
    public static string? ProjectPathFromUrl(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
            return null;
        var path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static string Redact(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;
        if (string.IsNullOrEmpty(uri.UserInfo))
            return url;

        var builder = new UriBuilder(uri) { UserName = "oauth2", Password = "***" };
        return builder.Uri.GetLeftPart(UriPartial.Path);
    }

    public static string? CommitUrl(string repositoryUrl, string commitSha)
    {
        if (string.IsNullOrWhiteSpace(commitSha) || commitSha.StartsWith("local-", StringComparison.Ordinal))
            return null;
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
            return null;

        var path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return $"{uri.Scheme}://{uri.Host}/{path}/-/commit/{commitSha}";
    }
}
