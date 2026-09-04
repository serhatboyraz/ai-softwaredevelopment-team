namespace AiDevAgent.Domain.Enums;

public enum WorkflowState
{
    Pending = 0,
    ContextCheck = 1,
    WaitingForInformation = 2,
    Analyzing = 3,
    Developing = 4,
    Testing = 5,
    Fixing = 6,
    ReadyForMr = 7,
    MrCreated = 8,
    WaitingForApproval = 9,
    Failed = 10,
    Cancelled = 11,
    Completed = 12
}

public enum TaskStatus
{
    Draft = 0,
    Queued = 1,
    InProgress = 2,
    WaitingForUser = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public enum AgentType
{
    Context = 0,
    Analysis = 1,
    Development = 2,
    Testing = 3,
    Git = 4
}

public enum WorkflowStepState
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3,
    Skipped = 4
}
