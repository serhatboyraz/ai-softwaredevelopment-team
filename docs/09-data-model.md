# Data Model

PostgreSQL is the system of record for workflow state.

## Entities

```text
Project
Task
Workflow
WorkflowStep
WorkflowEvent
AgentRun
ToolCall
TestRun
Artifact
MergeRequest
ApprovalRequest
```

## Relationships

```text
Project
  └── Task
       └── Workflow
            ├── WorkflowStep
            ├── WorkflowEvent
            ├── AgentRun
            ├── ToolCall
            ├── TestRun
            └── MergeRequest
```

Optional later: `pgvector` for semantic retrieval.
