using AiDevAgent.Application.Git;

namespace AiDevAgent.Application.Tests;

public class GitBranchesTests
{
    [Theory]
    [InlineData(null, "main")]
    [InlineData("", "main")]
    [InlineData("  ", "main")]
    [InlineData("develop", "develop")]
    [InlineData(" main ", "main")]
    public void OrFallback_UsesMainWhenMissing(string? input, string expected)
        => Assert.Equal(expected, GitBranches.OrFallback(input));
}
