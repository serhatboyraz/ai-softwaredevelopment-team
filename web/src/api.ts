export type Project = {
  id: string
  name: string
  gitLabProjectId: string
  repositoryUrl: string
  defaultBranch?: string | null
  description?: string | null
  createdAt: string
}

export type DevTask = {
  id: string
  projectId: string
  title: string
  description: string
  status: number
  createdAt: string
  jiraIssueKey?: string | null
  jiraIssueUrl?: string | null
}

export type JiraStatus = {
  configured: boolean
  baseUrl?: string | null
  defaultBoardId?: number | null
  message?: string | null
}

export type JiraBoard = {
  id: number
  name: string
  type: string
  projectKey?: string | null
}

export type JiraIssueSummary = {
  key: string
  id: string
  summary: string
  status: string
  issueType: string
  priority?: string | null
  assignee?: string | null
}

export type JiraComment = {
  author: string
  created: string
  body: string
}

export type JiraIssueDetails = {
  key: string
  id: string
  summary: string
  description: string
  formattedDescription: string
  status: string
  issueType: string
  priority?: string | null
  assignee?: string | null
  labels: string[]
  parentKey?: string | null
  parentSummary?: string | null
  url: string
  comments: JiraComment[]
}

export type JiraBoardWork = {
  boardId: number
  name: string
  type: string
  activeSprint?: {
    id: number
    name: string
    issues: JiraIssueSummary[]
  } | null
  boardIssues: JiraIssueSummary[]
  backlog: JiraIssueSummary[]
}

export type Workflow = {
  id: string
  taskId: string
  state: number
  correlationId: string
  repairAttempt: number
  maxRepairAttempts: number
  branchName?: string | null
  commitSha?: string | null
  errorMessage?: string | null
  createdAt: string
  startedAt?: string | null
  completedAt?: string | null
}

export type WorkflowEvent = {
  id: string
  eventType: string
  message?: string | null
  occurredAt: string
}

export type MergeRequest = {
  id: string
  gitLabIid?: number | null
  url?: string | null
  title: string
  state: string
}

export type TestRun = {
  id: string
  success: boolean
  buildPassed: boolean
  passed: number
  failed: number
  completedAt?: string | null
}

export type StepStatus = 'pending' | 'running' | 'waiting' | 'completed' | 'failed' | 'skipped'

export type WorkflowStepProgress = {
  key: string
  label: string
  status: StepStatus
  startedAt?: string | null
  completedAt?: string | null
  durationMs: number
  promptTokens: number
  completionTokens: number
  totalTokens: number
  summary?: string | null
}

export type AgentRun = {
  id: string
  agentType: number
  outputSummary?: string | null
  success: boolean
  startedAt?: string | null
  completedAt?: string | null
  promptTokens?: number | null
  completionTokens?: number | null
}

export type WorkflowDetail = {
  workflow: Workflow
  events: WorkflowEvent[]
  testRuns: TestRun[]
  steps: WorkflowStepProgress[]
  agentRuns: AgentRun[]
  mergeRequest?: MergeRequest | null
}

export const WORKFLOW_STATES: Record<number, string> = {
  0: 'Pending',
  1: 'Context check',
  2: 'Waiting for information',
  3: 'Analyzing',
  4: 'Developing',
  5: 'Testing',
  6: 'Fixing',
  7: 'Ready for MR',
  8: 'MR created',
  9: 'Waiting for approval',
  10: 'Failed',
  11: 'Cancelled',
  12: 'Completed',
}

const API_BASE = ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    headers: { 'Content-Type': 'application/json', ...(init?.headers ?? {}) },
    ...init,
  })
  if (!res.ok) {
    const text = await res.text()
    throw new Error(text || res.statusText)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export const api = {
  listProjects: () => request<Project[]>('/api/projects'),
  createProject: (body: {
    name: string
    gitLabProjectId: string
    repositoryUrl: string
  }) => request<Project>('/api/projects', { method: 'POST', body: JSON.stringify(body) }),
  deleteProject: (id: string) => request<void>(`/api/projects/${id}`, { method: 'DELETE' }),
  listTasks: (projectId: string) => request<DevTask[]>(`/api/projects/${projectId}/tasks`),
  createTask: (projectId: string, body: { title: string; description: string }) =>
    request<DevTask>(`/api/projects/${projectId}/tasks`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  jiraStatus: () => request<JiraStatus>('/api/jira/status'),
  jiraBoards: () => request<JiraBoard[]>('/api/jira/boards'),
  jiraBoardWork: (boardId: number) => request<JiraBoardWork>(`/api/jira/boards/${boardId}/work`),
  jiraIssue: (issueKey: string) => request<JiraIssueDetails>(`/api/jira/issues/${encodeURIComponent(issueKey)}`),
  startProjectWorkflow: (
    projectId: string,
    body: { title?: string; description?: string; jiraIssueKey?: string },
  ) =>
    request<Workflow>(`/api/projects/${projectId}/workflows`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),
  listWorkflows: () => request<Workflow[]>('/api/workflows'),
  getWorkflow: (id: string) => request<WorkflowDetail>(`/api/workflows/${id}`),
  startWorkflow: (taskId: string) =>
    request<Workflow>('/api/workflows', {
      method: 'POST',
      body: JSON.stringify({ taskId }),
    }),
  cancelWorkflow: (id: string) =>
    request<void>(`/api/workflows/${id}/cancel`, { method: 'POST' }),
  rerunWorkflow: (id: string) =>
    request<Workflow>(`/api/workflows/${id}/rerun`, { method: 'POST' }),
  sendWorkflowCommand: (id: string, command: string) =>
    request<Workflow>(`/api/workflows/${id}/commands`, {
      method: 'POST',
      body: JSON.stringify({ command }),
    }),
  approveWorkflow: (id: string, approved: boolean, comment?: string) =>
    request<void>(`/api/workflows/${id}/approve`, {
      method: 'POST',
      body: JSON.stringify({ approved, comment }),
    }),
}
