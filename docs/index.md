# AI Software Development Platform

This documentation is derived from the architecture specification (`ai-sdworkflow.md`).

The product is an **AI software development execution platform**: it accepts a GitLab repository and a development task, analyzes sufficiency of context, implements changes, builds and tests in an isolated Docker sandbox, then creates a Git branch, commit, push, and GitLab Merge Request.

!!! quote "Core principle"
    **The LLM provides reasoning; the platform provides control.**

[View the production infrastructure diagram](infrastructure.md){ .md-button .md-button--primary }

![AiDevAgent production infrastructure](assets/infrastructure.svg)

## Document index

| Doc | Description |
|-----|-------------|
| [Overview](01-overview.md) | Product overview, goals, non-goals |
| [Architecture](02-architecture.md) | High-level architecture, stack, layers |
| [Infrastructure](infrastructure.md) | Hosting, runtime, and dependency map |
| [Agents](03-agents.md) | Agent roles and typed contracts |
| [Workflow](04-workflow.md) | State machine, orchestration, HITL |
| [Tools and sandbox](05-tools-and-sandbox.md) | Tools, Docker sandbox, Git/GitLab |
| [API](06-api.md) | REST API and SignalR contracts |
| [Security](07-security.md) | Secrets, threat model, policies |
| [Frontend](08-frontend.md) | Workflow dashboard |
| [Data model](09-data-model.md) | PostgreSQL entities and persistence |
| [MVP](10-mvp.md) | MVP scope, phases, end-to-end path |
| [Decisions](11-decisions.md) | Architectural decisions |
| [Tasks](TASKS.md) | Detailed implementation task list |

## Repository layout

```text
AiDevAgent/
├── src/
│   ├── AiDevAgent.Api/
│   ├── AiDevAgent.Application/
│   ├── AiDevAgent.Domain/
│   ├── AiDevAgent.Infrastructure/
│   └── AiDevAgent.Agents/
├── prompts/
├── tests/
├── docker/
├── web/
├── docs/
├── mkdocs.yaml
├── docker-compose.yml
└── README.md
```

## Preview these docs

```bash
pip install -r requirements-docs.txt
python -m mkdocs serve
```

Then open `http://127.0.0.1:8000`.
