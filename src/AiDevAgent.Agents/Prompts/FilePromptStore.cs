using AiDevAgent.Application.Agents;
using Microsoft.Extensions.Configuration;

namespace AiDevAgent.Agents.Prompts;

public sealed class FilePromptStore(IConfiguration configuration) : IPromptStore
{
    public string Get(string agentName, string version = "v1")
    {
        var fileName = $"{version}.txt";
        foreach (var root in CandidateRoots())
        {
            var path = Path.Combine(root, agentName, fileName);
            if (File.Exists(path))
                return File.ReadAllText(path);
        }

        throw new FileNotFoundException($"Prompt not found: {agentName}/{fileName}");
    }

    private IEnumerable<string> CandidateRoots()
    {
        var configured = configuration["Prompts:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
            yield return configured;

        yield return Path.Combine(AppContext.BaseDirectory, "prompts");

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "prompts");
            dir = dir.Parent;
        }
    }
}
