namespace AiDevAgent.Domain.ValueObjects;

public static class BranchName
{
    public static string Create(long taskId, string slug)
    {
        var normalized = NormalizeSlug(slug);
        return $"ai/task-{taskId}-{normalized}";
    }

    public static string NormalizeSlug(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "change";

        var chars = input.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);

        return slug.Trim('-') is { Length: > 0 } s ? s[..Math.Min(s.Length, 48)] : "change";
    }
}
