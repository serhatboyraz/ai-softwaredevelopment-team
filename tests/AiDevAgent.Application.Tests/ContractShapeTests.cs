using AiDevAgent.Application.Contracts;

namespace AiDevAgent.Application.Tests;

public class ContractShapeTests
{
    [Fact]
    public void ContextResult_DefaultsReadyFalse()
    {
        var result = new ContextResult(false, 0.2, ["acceptance criteria"], [], ["What should happen on failure?"]);
        Assert.False(result.Ready);
        Assert.Single(result.Questions);
    }
}
