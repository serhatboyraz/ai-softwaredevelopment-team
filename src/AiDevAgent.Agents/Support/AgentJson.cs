using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiDevAgent.Agents.Support;

internal static class AgentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

internal sealed class ContextResultDto
{
    public bool Ready { get; set; }
    public double Confidence { get; set; }
    public List<string> MissingInformation { get; set; } = [];
    public List<string> RelevantFiles { get; set; } = [];
    public List<string> Questions { get; set; } = [];
}

internal sealed class AnalysisResultDto
{
    public string Summary { get; set; } = string.Empty;
    public string ArchitectureImpact { get; set; } = string.Empty;
    public List<string> AffectedFiles { get; set; } = [];
    public List<string> ImplementationSteps { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<string> TestStrategy { get; set; } = [];
}

internal sealed class DevelopmentResultDto
{
    public bool Success { get; set; } = true;
    public List<string> ChangedFiles { get; set; } = [];
    public string Summary { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = [];
    public List<FileWriteDto> Files { get; set; } = [];
}

internal sealed class FileWriteDto
{
    public string Path { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

internal sealed class GitMessageDto
{
    public string CommitMessage { get; set; } = string.Empty;
    public string MergeRequestTitle { get; set; } = string.Empty;
    public string MergeRequestDescription { get; set; } = string.Empty;
}
