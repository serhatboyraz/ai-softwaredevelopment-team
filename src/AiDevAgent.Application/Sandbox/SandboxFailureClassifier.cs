namespace AiDevAgent.Application.Sandbox;

/// <summary>
/// Distinguishes sandbox/package-feed outages from application compile/test failures.
/// </summary>
public static class SandboxFailureClassifier
{
    public static bool IsPackageFeedUnavailable(string? stdout, string? stderr)
        => ContainsMarker($"{stdout}{Environment.NewLine}{stderr}", PackageFeedMarkers);

    public static bool IsPackageFeedUnavailable(string? text)
        => ContainsMarker(text, PackageFeedMarkers);

    private static readonly string[] PackageFeedMarkers =
    [
        "NU1301",
        "NU1302",
        "NU1801",
        "Unable to load the service index",
        "unable to load the service index",
        "api.nuget.org",
        "temporary network/resource-unavailable",
        "npm ERR! network",
        "npm error network",
        "ECONNREFUSED",
        "ENOTFOUND registry.npmjs.org",
        "Could not transfer artifact"
    ];

    private static bool ContainsMarker(string? text, IReadOnlyList<string> markers)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        foreach (var marker in markers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
