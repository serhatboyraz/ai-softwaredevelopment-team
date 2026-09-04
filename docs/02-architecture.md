# Architecture

Production hosting, agents, and platform services are shown on the [infrastructure](infrastructure.md) page.

![AiDevAgent production infrastructure](assets/infrastructure.svg)

## High-level

```text
User → React Workflow Dashboard
         │ REST / SignalR
         ▼
      ASP.NET Core API (Control Plane)
         │
         ▼
      Background Worker / Workflow Executor
         │
         ▼
      Microsoft Agent Framework (orchestration)
         │
         ├── Context / Analysis / Development / Testing / Git Agents
         │
         ├── Azure OpenAI (via IChatClient abstraction)
         ├── Docker Sandbox
         └── GitLab
```

## Technology stack

| Layer | Choices |
|-------|---------|
| Backend | C#, ASP.NET Core, .NET, EF Core, Npgsql |
| AI | Microsoft Agent Framework, Azure OpenAI, `IChatClient` |
| Frontend | React, TypeScript, Vite, TanStack Query, SignalR, Tailwind |
| Data | PostgreSQL (optional `pgvector` later) |
| SCM | GitLab API + Git CLI |
| Execution | Docker isolated containers |
| Security | Azure Key Vault, Microsoft Entra ID |
| Observability | OpenTelemetry, Application Insights |
| Hosting | Azure Container Apps, ACR; Service Bus in production |

## Layer dependency direction

```text
Api → Application → Domain
Infrastructure → Application / Domain
Agents → Application / Domain
```

Domain must not depend on Azure OpenAI, GitLab SDK, Docker, EF Core, or ASP.NET Core.

## AI provider abstraction

Business logic depends on `IChatClient` / application AI abstractions, not provider SDKs.

Azure OpenAI is the initial provider. Configuration example:

```json
{
  "AI": {
    "Provider": "AzureOpenAI",
    "Deployment": "coding-model"
  }
}
```

## Background execution

Workflows must not run inside HTTP requests.

- **MVP:** API → `BackgroundService` → Workflow Executor
- **Production:** API → Azure Service Bus → Worker → Agent Framework

API creates the workflow and returns immediately.

## Scaling phases

1. **MVP** — single ASP.NET Core app, one worker, one workflow, one AOAI deployment
2. **Production** — Service Bus, worker pool, distributed locks, richer observability
3. **Advanced** — split agent services only when scaling/security/lifecycle justify it
