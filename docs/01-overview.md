# Overview

## What this is

An AI-powered software development **execution** platform (not a chat app).

Primary flow:

```text
User → React + TypeScript → ASP.NET Core API → Microsoft Agent Framework → Azure OpenAI
         ├── Context Agent
         ├── Analysis Agent
         ├── Development Agent
         ├── Testing Agent
         └── Git Agent
```

Supporting infrastructure: PostgreSQL, Docker Sandbox, GitLab, Azure Key Vault, SignalR, OpenTelemetry / Application Insights, Azure Container Apps. See [Infrastructure](infrastructure.md).

The initial implementation does **not** depend on Microsoft Foundry Agent Service. The application owns the agent runtime and workflow execution.

## Architectural goals

- Accept software development tasks from users
- Connect securely to GitLab repositories
- Determine whether a task has enough context to begin
- Analyze the repository before changing code
- Produce an implementation plan
- Modify source code through controlled tools
- Execute builds and tests in an isolated Docker sandbox
- Diagnose failures and attempt fixes (bounded retries)
- Create a dedicated Git branch, commit, push
- Create a GitLab Merge Request
- Persist workflow state and events
- Provide live workflow progress to the frontend
- Support human approval when required
- Make AI model providers replaceable
- Keep secrets out of prompts, logs, source code, and the frontend
- Support retries, cancellation, checkpoints, and recovery
- Provide production-grade observability

## Non-goals (MVP)

- Automatically merge Merge Requests
- Execute arbitrary commands directly on the application host
- Allow agents unrestricted access to production systems
- Give the LLM direct access to GitLab credentials
- Build a fully autonomous multi-agent chat system
- Depend on a single AI provider implementation
- Run long-running workflows inside HTTP request lifetimes
- Trust repository instructions as system-level instructions
