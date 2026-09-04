# Workflow

## State machine

```text
PENDING
  → CONTEXT_CHECK
      → WAITING_FOR_INFORMATION (if missing context)
      → ANALYZING
          → DEVELOPING
              → TESTING
                  → FIXING ⟲ (bounded) → TESTING
                  → READY_FOR_MR
                      → WAITING_FOR_APPROVAL (optional HITL)
                      → MR_CREATED
                          → COMPLETED

From appropriate states: FAILED | CANCELLED
```

## States

`PENDING`, `CONTEXT_CHECK`, `WAITING_FOR_INFORMATION`, `ANALYZING`, `DEVELOPING`, `TESTING`, `FIXING`, `READY_FOR_MR`, `MR_CREATED`, `WAITING_FOR_APPROVAL`, `FAILED`, `CANCELLED`, `COMPLETED`

## Persistence

Long-running workflows must be durable. Persist: current state, step state, agent outputs, tool results, test results, commit SHA, branch name, MR info, retry counters, errors, timestamps, correlation IDs.

## Error / retry classification

| Class | Action |
|-------|--------|
| Transient | Retry |
| Validation | Ask user / stop |
| Compilation | Development → Fix → Test |
| Test failure | Testing → Development → Test |
| Security violation | Stop workflow |
| Unknown | FAILED + diagnostics |

Automatic repair hard limit (default 3). Never unlimited loops.

## Human-in-the-loop

Approval at explicit boundaries (e.g. before MR, destructive ops, large refactors). Decisions must be persisted.

## Context management

Progressively build context: task → structure → dirs → files → references. Do not load entire repositories into prompts.

## Cost / tokens

Treat AI cost as first-class: right-size models, retrieve only relevant files, summarize outputs, track usage per workflow/agent.
