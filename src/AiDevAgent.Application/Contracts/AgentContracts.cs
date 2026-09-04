namespace AiDevAgent.Application.Contracts;

public sealed record ContextResult(
    bool Ready,
    double Confidence,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> RelevantFiles,
    IReadOnlyList<string> Questions);

public sealed record AnalysisResult(
    string Summary,
    string ArchitectureImpact,
    IReadOnlyList<string> AffectedFiles,
    IReadOnlyList<string> ImplementationSteps,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> TestStrategy);

public sealed record DevelopmentResult(
    bool Success,
    IReadOnlyList<string> ChangedFiles,
    string Summary,
    IReadOnlyList<string> Notes);

public sealed record TestResult(
    bool Success,
    bool BuildPassed,
    int Passed,
    int Failed,
    IReadOnlyList<string> Failures,
    bool InfrastructureFailure = false);

public sealed record GitResult(
    bool Success,
    string Branch,
    string CommitSha,
    string? MergeRequestUrl);
