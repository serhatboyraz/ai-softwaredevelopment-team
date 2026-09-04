# Agents

Agents are specialized components inside a deterministic workflow — not independent chatbots.

## Context Agent

Determines whether the task has enough information to begin.

- Read task, inspect repo structure, detect project type
- Locate docs, identify affected files, ask focused questions when needed
- Stop workflow when critical information is missing

```csharp
public sealed record ContextResult(
    bool Ready,
    double Confidence,
    IReadOnlyList<string> MissingInformation,
    IReadOnlyList<string> RelevantFiles,
    IReadOnlyList<string> Questions);
```

## Analysis Agent

Understands the required implementation before modifying code.

```csharp
public sealed record AnalysisResult(
    string Summary,
    string ArchitectureImpact,
    IReadOnlyList<string> AffectedFiles,
    IReadOnlyList<string> ImplementationSteps,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> TestStrategy);
```

## Development Agent

Implements the approved plan through controlled tools.

- Create/modify files, follow repo patterns, avoid unnecessary refactoring
- Max automatic repair attempts configurable (MVP default ~3); no infinite loops

## Testing Agent

Verifies implementation via real sandbox execution.

```csharp
public sealed record TestResult(
    bool Success,
    bool BuildPassed,
    int Passed,
    int Failed,
    IReadOnlyList<string> Failures);
```

**The sandbox is authoritative.** LLM reasoning is not a substitute for execution.

## Git Agent

Creates branch, commit, push, and GitLab MR (does not auto-merge in MVP).

Branch: `ai/task-{taskId}-{slug}` (e.g. `ai/task-184-add-user-search`)

```csharp
public sealed record GitResult(
    bool Success,
    string Branch,
    string CommitSha,
    string? MergeRequestUrl);
```

## Development result contract

```csharp
public sealed record DevelopmentResult(
    bool Success,
    IReadOnlyList<string> ChangedFiles,
    string Summary,
    IReadOnlyList<string> Notes);
```

## Orchestrator vs agents

| Orchestrator (deterministic) | Agents (reasoning) |
|------------------------------|--------------------|
| State transitions, retries, timeouts | Understanding requirements |
| Permissions, checkpoints, cancellation | Planning, code generation |
| Error handling, human approvals | Failure diagnosis, review suggestions |

The LLM must not decide whether a privileged operation is allowed.

## Prompts

Versioned under `prompts/{agent}/vN.txt`. Separate system rules, platform policies, task info, repository data, tool results, and output format. Repository content must never be concatenated into privileged system instructions.
