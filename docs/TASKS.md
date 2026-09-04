# Implementation Task List

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done · `[-]` deferred

Last updated: 2026-09-04 (agent executor sprint)

---

## Phase 0 — Foundation

### 0.1 Documentation
- [x] Convert architecture spec into `docs/`
- [x] Create this detailed task list
- [x] Root `README.md` with run instructions
- [x] Keep `ai-sdworkflow.md` as source architecture reference
- [x] MkDocs site (`mkdocs.yaml`, `docs/index.md`, infrastructure diagram)

### 0.2 Solution scaffold
- [x] Create solution (`AiDevAgent.slnx` — .NET 10 format)
- [x] Create projects: Api, Application, Domain, Infrastructure, Agents
- [x] Create test projects: Domain.Tests, Application.Tests
- [x] Create test project: Integration.Tests
- [x] Wire project references (clean architecture)
- [x] Add `.gitignore`, `.editorconfig`
- [x] Add `Directory.Build.props`
- [x] Add `docker-compose.yml` (postgres)
- [x] Placeholder `prompts/{agent}/v1.txt` (all five agents)
- [x] Scaffold `web/` Vite React + TS app

---

## Phase 1 — Domain & Persistence

### 1.1 Domain entities
- [x] `Project` (name, GitLab project id/url, timestamps)
- [x] `DevTask` (title, description, status, ProjectId)
- [x] `Workflow` (state, TaskId, retry counters, branch, commit, correlation)
- [x] `WorkflowStep` (name, state, timestamps, payload)
- [x] `WorkflowEvent` (type, payload, timestamp)
- [x] `AgentRun` (agent type, input/output summaries, duration)
- [x] `ToolCall` (tool name, args summary, result summary, success)
- [x] `TestRun` (build/test results)
- [x] `Artifact` (path, kind, metadata)
- [x] `MergeRequest` (iid, url, title, state)
- [x] `ApprovalRequest` (status, decidedBy, decidedAt)

### 1.2 Enums & value objects
- [x] `WorkflowState` enum (all states from docs)
- [x] `TaskStatus`, `ApprovalStatus`, `AgentType`, `WorkflowStepState`
- [ ] Dedicated `EventType` enum (currently string event types)
- [x] Branch naming helper: `ai/task-{taskId}-{slug}`

### 1.3 Domain interfaces
- [x] `IProjectRepository`, `IDevTaskRepository`, `IWorkflowRepository`
- [x] `IUnitOfWork`
- [x] `IWorkflowEventPublisher` (SignalR + logging)
- [x] `IWorkflowQueue`, `IWorkflowExecutor`
- [x] `IChatClient` / AI abstraction interfaces (`IAiRuntime` + MEAI `IChatClient`)
- [x] Tool interfaces: repository, git, gitlab, sandbox
- [x] `ISandboxExecutor`, `IWorkspaceManager`, `ISecretProvider`
- [x] `IRepositoryTools`, `IGitService`, `IGitLabClient`

### 1.4 Persistence
- [x] EF Core `AppDbContext`
- [x] Entity configurations (fluent API in `OnModelCreating`)
- [x] Npgsql connection (Postgres provider)
- [x] SQLite fallback for local Development
- [ ] EF Core Migrations (currently `EnsureCreated` for MVP)
- [x] Repository implementations
- [x] Health check endpoint (`GET /health`)
- [ ] Seed data

---

## Phase 2 — API Control Plane

### 2.1 ASP.NET Core API
- [x] `Program.cs` DI composition
- [x] Controllers for projects, tasks, workflows, merge-requests
- [x] DTOs + mapping (no EF entities in responses)
- [x] `POST/GET /api/projects`
- [x] `POST/GET /api/projects/{id}/tasks`
- [x] `POST/GET /api/workflows`, `GET /api/workflows/{id}`
- [x] `POST /api/workflows/{id}/cancel`
- [x] `POST /api/workflows/{id}/approve`
- [x] `GET /api/workflows/{id}/events`
- [x] `GET /api/workflows/{id}/tests`
- [x] `GET /api/merge-requests`
- [~] Validation (basic required-field checks; Problem Details not fully wired)
- [x] CORS for local web
- [x] OpenAPI (`MapOpenApi` in Development)

### 2.2 Background execution
- [x] `WorkflowBackgroundService` / in-memory channel queue
- [x] Enqueue on workflow create; return `202 Accepted` immediately
- [x] Cancellation token plumbing on worker loop
- [x] Checkpoint write after each major state (resumes from current state on re-enqueue; worker restart still loses in-memory queue)
- [x] Agent workflow executor (deterministic orchestrator; offline heuristics when Azure OpenAI is not configured)

### 2.3 SignalR
- [x] `WorkflowHub` at `/workflowHub`
- [x] Group join `workflow:{id}` (`Subscribe` / `Unsubscribe`)
- [x] Publish events from orchestrator (`SignalRWorkflowEventPublisher`)
- [x] Client reconnect + REST state rebuild (web: SignalR + polling invalidate)

---

## Phase 3 — Integrations

### 3.1 GitLab
- [x] GitLab API client (projects, create MR) — `GitLabClient`
- [x] Config + token from secrets/env (`GitLab:Token` / `ISecretProvider`)
- [x] Never pass token into prompts (token only on HttpClient header)
- [ ] Add MR comment tool
- [x] Wire client into Git Agent (real MR when token + cloned repo; stub URL otherwise)

### 3.2 Git / workspace
- [x] Workspace manager (`IWorkspaceManager` → `.workspaces/{workflowId}`)
- [x] Git CLI service: clone, branch, status, diff, commit, push, rev-parse
- [x] Per-workflow isolated workspace path
- [x] Cleanup method on workspace manager
- [x] Wire clone into workflow executor (opt-in via `Workflow:EnableGitClone`)
- [x] Credential helper for private repos (oauth2 token embedded in clone URL; never logged)

### 3.3 Docker sandbox
- [x] Sandbox runner service (`DockerSandboxExecutor`)
- [x] Detect build/test commands by project type (`ProjectTypeDetector`)
- [x] Resource limits (memory/cpus), timeout, `--network none`
- [x] Capture stdout/stderr with size limits
- [x] Local fallback flag for Dev (`Sandbox:AllowLocalFallback`; also on docker-daemon errors)
- [x] Build + Test smoke wired into Testing Agent (`Workflow:EnableSandboxSmoke`)
- [x] Full Build + Test tools for Testing Agent (detected commands in sandbox)

### 3.4 Secrets
- [x] Local: configuration / env (`ConfigurationSecretProvider`)
- [ ] Azure Key Vault provider (abstraction ready via `ISecretProvider`)
- [ ] Secret redaction in logs

---

## Phase 4 — AI & Agents

### 4.1 AI provider
- [x] `IChatClient` application abstraction (`IAiRuntime` + MEAI `IChatClient`)
- [x] Azure OpenAI adapter (`AzureOpenAiRuntime`; API key or DefaultAzureCredential)
- [x] Configuration: Provider, Deployment, Endpoint, ApiKey
- [x] Token usage recording hooks (`AgentRun.PromptTokens` / `CompletionTokens`)

### 4.2 Microsoft Agent Framework
- [x] Package references (`Microsoft.Agents.AI`) + `ChatClientAgent` per role
- [x] Typed MVP path in `AgentWorkflowExecutor` (deterministic orchestrator; agents reason)
- [ ] Checkpointing hooks to PostgreSQL (MAF checkpoints; app already persists state per step)
- [x] Human-in-the-loop step wiring (`Workflow:RequireApprovalBeforeMr` → WaitingForApproval)

### 4.3 Context Agent
- [x] Prompt `prompts/context/v1.txt`
- [x] Tools: list/read/search files, detect project type
- [x] Structured `ContextResult` contract defined
- [x] Transition: Ready → ANALYZING | else WAITING_FOR_INFORMATION

### 4.4 Analysis Agent
- [x] Prompt `prompts/analysis/v1.txt`
- [x] `AnalysisResult` contract defined
- [x] Produce + persist analysis; hand off to Development Agent

### 4.5 Development Agent
- [x] Prompt `prompts/development/v1.txt`
- [x] `DevelopmentResult` contract defined
- [x] Write/edit tools only in workspace
- [ ] Focused diffs; formatting where appropriate (model-dependent)

### 4.6 Testing Agent
- [x] Prompt `prompts/testing/v1.txt`
- [x] `TestResult` contract defined
- [x] Invoke sandbox build/test only
- [x] On failure → FIXING (bounded) → Development → Testing

### 4.7 Git Agent
- [x] Prompt `prompts/git/v1.txt`
- [x] `GitResult` contract defined
- [x] Real branch naming, commit, push, create MR (when clone + GitLab token)
- [~] Pre-MR deterministic checklist (branch convention; tests-passed flag; no secret scan yet)

---

## Phase 5 — Orchestration polish

- [x] Full state machine transitions (agent gates + offline heuristics)
- [ ] Retry classification (transient vs terminal)
- [x] Max repair attempts field on workflow (default 3; enforced)
- [x] Approval API + entity; gate before MR when `Workflow:RequireApprovalBeforeMr`
- [x] Cancellation endpoint (active → Cancelled)
- [ ] Recovery after worker restart from checkpoint
- [x] Correlation IDs on workflow entity

---

## Phase 6 — Frontend dashboard

- [x] Vite + React + TS + Tailwind + TanStack Query
- [x] Projects list / create
- [x] Task create for project
- [x] Workflow detail: state, timeline
- [ ] Agents / tools panels (richer detail)
- [x] Build/test results panel
- [x] Branch / commit / MR link
- [x] Approval actions UI
- [x] SignalR live updates + reconnect (+ REST polling fallback)
- [x] Safe summaries only (event messages; no CoT)

---

## Phase 7 — Observability & hardening

- [ ] OpenTelemetry instrumentation
- [ ] Application Insights exporter (optional locally)
- [~] Structured logging present; correlation id scoped on executor logs
- [ ] Metrics (durations, retries, tokens, MR rate)
- [ ] Security logging for tool auth denials
- [ ] Prompt injection defenses (repo as data)
- [ ] Secret scanning before commit/MR

---

## Phase 8 — Tests

### Unit
- [x] Branch naming tests
- [x] Agent contract shape smoke test
- [x] State transitions, retry limits (waiting-for-info, approval gate, happy path)
- [ ] Validation, error classification, permissions

### Integration
- [x] Health endpoint via `WebApplicationFactory`
- [x] Repository tools path-escape + round-trip
- [x] Offline agent workflow smoke (project → task → Completed + stub MR)
- [ ] PostgreSQL repositories
- [ ] GitLab (mocked or test project)
- [ ] Docker sandbox smoke
- [ ] AOAI adapter (mocked)
- [ ] SignalR hub smoke

### E2E
- [x] Manual / automated smoke: project → task → workflow → Completed + MR (offline agents)
- [ ] Sample repo + task through Context → MR (real agents)
- [ ] Waiting-for-information path
- [ ] Fix loop then success
- [ ] Cancel mid-workflow

---

## Phase 9 — Deploy

- [ ] Docker images for API (+ worker if split)
- [ ] ACR + Container Apps manifests
- [ ] Managed identity → Key Vault
- [ ] Production Service Bus handoff
- [ ] Frontend Static Web Apps / Container Apps
- [ ] Staging environment config

---

## Progress summary

| Phase | Status |
|-------|--------|
| 0 Foundation | Done |
| 1 Domain & Persistence | Done for MVP (migrations remain) |
| 2 API Control Plane | Done for MVP |
| 3 Integrations | Git clone/MR + sandbox + project-type detection |
| 4 AI & Agents | Agent Framework agents + Azure OpenAI; offline heuristics without keys |
| 5 Orchestration polish | State machine, repair loop, approval gate |
| 6 Frontend | Projects/tasks/workflows + SignalR + tests/approval |
| 7–9 | Not started |

## Current sprint (next up)

1. [x] Docs + task list
2. [x] Solution + Domain + Persistence + API skeleton
3. [x] docker-compose Postgres + SQLite dev fallback
4. [x] Workflow enqueue stub (BackgroundService)
5. [x] Basic React shell
6. [x] GitLab + workspace + Docker sandbox foundations
7. [x] Wire workspace/git/sandbox into stub executor (clone opt-in; sandbox smoke on)
8. [x] Azure OpenAI + Agent Framework agents (replace stub)
9. [x] Wire real GitLab MR creation into Git step
10. [ ] MAF checkpointing to PostgreSQL + worker restart recovery
11. [ ] Secret scanning before commit/MR + GitLab MR comments
12. [ ] Sample repo E2E with live Azure OpenAI

Track progress by checking boxes as work lands.
