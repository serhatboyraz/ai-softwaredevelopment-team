# AI Software Development Platform (AiDevAgent)

AI-powered software development **execution** platform: GitLab repo + task → analyze → implement → sandbox build/test → branch/commit/push → Merge Request.

> The LLM provides reasoning; the platform provides control.

## Documentation

Architecture docs are MkDocs Material under [`docs/`](docs/). Start at [`docs/index.md`](docs/index.md); the full checklist is [`docs/TASKS.md`](docs/TASKS.md).  
Source architecture: [`ai-sdworkflow.md`](ai-sdworkflow.md).

```bash
pip install -r requirements-docs.txt
python -m mkdocs serve
```

## Solution layout

```text
src/
  AiDevAgent.Api/            # ASP.NET Core control plane + SignalR
  AiDevAgent.Application/    # Workflow services, DTOs, contracts
  AiDevAgent.Domain/         # Entities, enums, interfaces
  AiDevAgent.Infrastructure/ # EF Core/PostgreSQL, queue, SignalR publisher
  AiDevAgent.Agents/         # Context / Analysis / Development / Testing / Git agents
web/                         # React dashboard
prompts/                     # Versioned agent prompts
docs/                        # Spec → docs + tasks
```

## Prerequisites

- .NET 10 SDK
- Docker (PostgreSQL)
- Node 20+ (frontend, later)

## Quick start

```bash
# API — local SQLite (default in Development)
dotnet run --project src/AiDevAgent.Api

# Web dashboard (proxies API on :5284)
cd web && npm install && npm run dev
# → http://localhost:5173

# PostgreSQL (optional)
docker compose up -d postgres
```

OpenAPI is available in Development at `/openapi/v1.json`.

## Current MVP status

Runnable end-to-end (offline heuristics if Azure OpenAI is not configured):

- Clean architecture solution
- Domain model + EF Core / PostgreSQL (SQLite in Development)
- REST API for projects, tasks, workflows
- In-memory workflow queue + `BackgroundService`
- **Agent workflow executor** (Context → Analysis → Development → Testing → Git)
- Azure OpenAI via Microsoft Agent Framework (`ChatClientAgent`); tools stay inside the workspace
- Git clone / branch / commit / push + GitLab MR when `Workflow:EnableGitClone` and `GitLab:Token` are set
- SignalR hub at `/workflowHub`

## Azure OpenAI (optional)

Without keys, agents use offline heuristics so the dashboard still completes a stub workflow.

```bash
cd src/AiDevAgent.Api
dotnet user-secrets set "AI:Endpoint" "https://YOUR-RESOURCE.openai.azure.com/"
dotnet user-secrets set "AI:Deployment" "coding-model"
dotnet user-secrets set "AI:ApiKey" "YOUR-KEY"
dotnet user-secrets set "GitLab:Token" "YOUR-GITLAB-TOKEN"
```

Set `Workflow:EnableGitClone` to `true` to clone the project repo. Leave `AI:ApiKey` empty to use `DefaultAzureCredential` against the endpoint.

## Jira (optional)

Add your Jira Cloud site and API token to `appsettings.json` / `appsettings.Development.json` (or user secrets):

```json
"Jira": {
  "BaseUrl": "https://your-domain.atlassian.net",
  "Email": "you@example.com",
  "Token": "YOUR-JIRA-API-TOKEN",
  "BoardId": 12,
  "ProjectKey": "PROJ"
}
```

`BoardId` and `ProjectKey` are optional. Create a token at [Atlassian API tokens](https://id.atlassian.com/manage-profile/security/api-tokens).

On a project page you can:

- Pick an issue from the **active board** or **backlog** — the platform loads the Jira summary, description, and comments, then starts a workflow
- Or **write a task name and description** and start a workflow without Jira

## Example API flow

```http
POST /api/projects
{ "name": "Demo", "gitLabProjectId": "123", "repositoryUrl": "https://gitlab.com/org/demo.git" }

POST /api/projects/{id}/tasks
{ "title": "Add health check", "description": "Add a /health endpoint returning 200." }

POST /api/workflows
{ "taskId": "{task-guid}" }

GET /api/workflows/{id}
```
