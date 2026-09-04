# API and Real-time

## REST endpoints

```http
POST /api/projects
GET  /api/projects

POST /api/projects/{id}/tasks
GET  /api/projects/{id}/tasks

POST /api/workflows
GET  /api/workflows
GET  /api/workflows/{id}

POST /api/workflows/{id}/cancel
POST /api/workflows/{id}/approve

GET /api/workflows/{id}/events
GET /api/workflows/{id}/tests

GET /api/merge-requests
```

Use DTOs/contracts — do not expose EF entities.

## SignalR

Hub: `/workflowHub`  
Groups: `workflow:{workflowId}`

Events (examples):

- `workflow.started` / `completed` / `failed`
- `step.started` / `completed` / `failed`
- `agent.started` / `completed`
- `tool.started` / `completed` / `failed`
- `build.started` / `completed`
- `test.started` / `completed`
- `git.branch_created` / `commit_created` / `pushed`
- `mr.created`
- `human.approval_required`

SignalR delivers live updates; PostgreSQL is authoritative. Frontend must reconnect and reconstruct state from the API.

## Observability IDs

Correlate: `workflowId`, `taskId`, `agentRunId`, `toolCallId`.

Metrics: workflow/agent/tool/build/test duration, retries, failures, token usage, model latency, estimated cost, MR success rate.
