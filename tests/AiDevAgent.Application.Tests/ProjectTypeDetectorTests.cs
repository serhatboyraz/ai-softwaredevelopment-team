using AiDevAgent.Application.Git;
using AiDevAgent.Application.ProjectType;

namespace AiDevAgent.Application.Tests;

public class ProjectTypeDetectorTests
{
    [Fact]
    public void DetectsDotnetFromCsproj()
    {
        var profile = ProjectTypeDetector.Detect(["src/App/App.csproj", "README.md"]);
        Assert.Equal("dotnet", profile.Kind);
        Assert.Contains("dotnet build", profile.BuildCommand);
        Assert.Contains("dotnet test", profile.TestCommand);
    }

    [Fact]
    public void DetectsNodeFromPackageJson()
    {
        var profile = ProjectTypeDetector.Detect(["package.json", "src/index.ts"]);
        Assert.Equal("node", profile.Kind);
    }

    [Fact]
    public void UnknownWhenNoMarkers()
    {
        var profile = ProjectTypeDetector.Detect(["notes.txt"]);
        Assert.Equal("unknown", profile.Kind);
        Assert.Null(profile.BuildCommand);
    }
}

public class GitCloneUrlTests
{
    [Fact]
    public void EmbedsOauth2Token()
    {
        var url = GitCloneUrl.WithOptionalToken("https://gitlab.com/org/repo.git", "secret-token");
        Assert.Contains("oauth2", url, StringComparison.Ordinal);
        Assert.Contains("secret-token", url, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactHidesUserInfo()
    {
        var redacted = GitCloneUrl.Redact("https://oauth2:secret-token@gitlab.com/org/repo.git");
        Assert.DoesNotContain("secret-token", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectPathFromHttpsCloneUrl()
    {
        Assert.Equal("org/repo", GitCloneUrl.ProjectPathFromUrl("https://gitlab.com/org/repo.git"));
        Assert.Equal("group/sub/repo", GitCloneUrl.ProjectPathFromUrl("https://gitlab.com/group/sub/repo"));
        Assert.Null(GitCloneUrl.ProjectPathFromUrl("not-a-url"));
    }

    [Fact]
    public void CommitUrl_FromHttpsCloneUrl()
    {
        var url = GitCloneUrl.CommitUrl("https://gitlab.com/org/repo.git", "abc123def");
        Assert.Equal("https://gitlab.com/org/repo/-/commit/abc123def", url);
    }
}
