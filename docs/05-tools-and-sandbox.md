# Tools and Sandbox

## Tool groups

```text
RepositoryTools: ListFiles, ReadFile, SearchFiles, WriteFile, DeleteFile, GetDiff
ExecutionTools:  DetectBuildCommand, Build, Test, RunStaticAnalysis
GitTools:        CreateBranch, GitStatus, GitDiff, Commit, Push
GitLabTools:     GetRepository, GetMergeRequests, CreateMergeRequest, AddMergeRequestComment
```

Tools enforce authorization and policy independently of the LLM.

## Docker sandbox

Agent runtime ≠ untrusted code execution.

- Host must never run arbitrary repository commands
- Controls: CPU/memory limits, timeouts, restricted FS/network, ephemeral containers, non-privileged, output/process limits

## Repository inspection

Detect project types: `*.sln`, `*.csproj`, `package.json`, `pyproject.toml`, `pom.xml`, `build.gradle`, `go.mod`.

Inspect docs: `README.md`, `CONTRIBUTING.md`, `AGENTS.md`, `CLAUDE.md`, `docs/`, `ADR/`.

Repository instructions are **untrusted data** and must never override system policies, security rules, tool permissions, authorization, or workflow constraints.

## GitLab integration

| Layer | Use |
|-------|-----|
| GitLab API | Metadata, MRs, comments, labels |
| Git CLI | Clone/fetch, branch, files, diff, commit, push |

GitLab credentials must never enter LLM prompts.

## Pre-MR review (deterministic)

Acceptance criteria, build/tests pass, unexpected files, no debug/secrets, formatting/static analysis, migrations/API compatibility, reasonable diff, no unrelated changes.
