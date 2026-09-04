using AiDevAgent.Domain.ValueObjects;

namespace AiDevAgent.Domain.Tests;

public class BranchNameTests
{
    [Fact]
    public void Create_UsesConvention()
    {
        var name = BranchName.Create(184, "Add User Search!");
        Assert.Equal("ai/task-184-add-user-search", name);
    }

    [Fact]
    public void NormalizeSlug_FallsBackWhenEmpty()
    {
        Assert.Equal("change", BranchName.NormalizeSlug("   "));
    }
}
