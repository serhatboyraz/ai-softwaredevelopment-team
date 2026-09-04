import { useCallback, useState } from 'react'
import { Link, Route, Routes, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, WORKFLOW_STATES } from './api'
import { JiraPanel } from './JiraPanel'
import { useWorkflowHub, type LiveEvent } from './useWorkflowHub'
import { WorkflowDiagram } from './WorkflowDiagram'

function Shell({ children, wide }: { children: React.ReactNode; wide?: boolean }) {
  return (
    <div className="min-h-full">
      <header className="border-b border-[var(--border)] px-6 py-4 flex items-center justify-between">
        <Link to="/" className="text-lg font-semibold tracking-tight no-underline text-[var(--text)]">
          AiDevAgent
        </Link>
        <nav className="flex gap-4 text-sm text-[var(--muted)]">
          <Link to="/" className="hover:text-[var(--text)]">Projects</Link>
          <Link to="/workflows" className="hover:text-[var(--text)]">Workflows</Link>
        </nav>
      </header>
      <main className={`mx-auto px-6 py-8 ${wide ? 'max-w-7xl' : 'max-w-5xl'}`}>{children}</main>
    </div>
  )
}

function ProjectsPage() {
  const qc = useQueryClient()
  const navigate = useNavigate()
  const { data: projects = [], isLoading } = useQuery({
    queryKey: ['projects'],
    queryFn: api.listProjects,
  })

  const [name, setName] = useState('')
  const [repo, setRepo] = useState('')
  const [gitlabId, setGitlabId] = useState('')

  const create = useMutation({
    mutationFn: () =>
      api.createProject({
        name,
        repositoryUrl: repo,
        gitLabProjectId: gitlabId || '0',
      }),
    onSuccess: (p) => {
      void qc.invalidateQueries({ queryKey: ['projects'] })
      navigate(`/projects/${p.id}`)
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.deleteProject(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['projects'] }),
  })

  return (
    <Shell>
      <h1 className="text-2xl font-semibold mb-2">Projects</h1>
      <p className="text-[var(--muted)] mb-8 text-sm">
        Connect a GitLab repository, pick a Jira issue or write a task, and run an AI development workflow.
      </p>

      <section className="mb-10 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-5">
        <h2 className="text-sm font-medium mb-4 uppercase tracking-wide text-[var(--muted)]">New project</h2>
        <div className="grid gap-3 sm:grid-cols-3">
          <input
            className="rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm"
            placeholder="Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <input
            className="rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm sm:col-span-2"
            placeholder="Repository URL"
            value={repo}
            onChange={(e) => setRepo(e.target.value)}
          />
          <input
            className="rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm"
            placeholder="GitLab project ID"
            value={gitlabId}
            onChange={(e) => setGitlabId(e.target.value)}
          />
          <button
            type="button"
            disabled={!name || !repo || create.isPending}
            onClick={() => create.mutate()}
            className="rounded bg-[var(--accent)] px-4 py-2 text-sm font-medium text-[var(--bg)] disabled:opacity-40"
          >
            Create
          </button>
        </div>
        {create.isError && (
          <p className="mt-3 text-sm text-[var(--danger)]">{(create.error as Error).message}</p>
        )}
      </section>

      {isLoading ? (
        <p className="text-[var(--muted)]">Loading…</p>
      ) : projects.length === 0 ? (
        <p className="text-[var(--muted)]">No projects yet.</p>
      ) : (
        <>
          {remove.isError && (
            <p className="mb-3 text-sm text-[var(--danger)]">{(remove.error as Error).message}</p>
          )}
          <ul className="space-y-2">
            {projects.map((p) => (
              <li
                key={p.id}
                className="flex items-stretch gap-2 rounded-lg border border-[var(--border)] bg-[var(--surface)] hover:border-[var(--accent)]"
              >
                <Link
                  to={`/projects/${p.id}`}
                  className="min-w-0 flex-1 px-4 py-3 no-underline text-[var(--text)]"
                >
                  <div className="font-medium">{p.name}</div>
                  <div className="mono text-xs text-[var(--muted)] mt-1 truncate">{p.repositoryUrl}</div>
                </Link>
                <button
                  type="button"
                  disabled={remove.isPending}
                  onClick={() => {
                    if (!confirm(`Delete “${p.name}”? This also removes its tasks and workflows.`)) return
                    remove.mutate(p.id)
                  }}
                  className="m-2 self-center shrink-0 rounded border border-[var(--danger)] px-3 py-1.5 text-sm text-[var(--danger)] hover:bg-[var(--danger)]/10 disabled:opacity-40"
                >
                  {remove.isPending && remove.variables === p.id ? 'Deleting…' : 'Delete'}
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </Shell>
  )
}

function ProjectDetailPage() {
  const { projectId = '' } = useParams()
  const qc = useQueryClient()
  const navigate = useNavigate()

  const { data: projects = [] } = useQuery({ queryKey: ['projects'], queryFn: api.listProjects })
  const project = projects.find((p) => p.id === projectId)

  const { data: tasks = [] } = useQuery({
    queryKey: ['tasks', projectId],
    queryFn: () => api.listTasks(projectId),
    enabled: !!projectId,
  })

  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [startMode, setStartMode] = useState<'jira' | 'manual'>('jira')

  const createTask = useMutation({
    mutationFn: () => api.createTask(projectId, { title, description }),
    onSuccess: () => {
      setTitle('')
      setDescription('')
      void qc.invalidateQueries({ queryKey: ['tasks', projectId] })
    },
  })

  const startManual = useMutation({
    mutationFn: () => api.startProjectWorkflow(projectId, { title, description }),
    onSuccess: (wf) => {
      setTitle('')
      setDescription('')
      void qc.invalidateQueries({ queryKey: ['tasks', projectId] })
      navigate(`/workflows/${wf.id}`)
    },
  })

  const startWorkflow = useMutation({
    mutationFn: (taskId: string) => api.startWorkflow(taskId),
    onSuccess: (wf) => navigate(`/workflows/${wf.id}`),
  })

  const remove = useMutation({
    mutationFn: () => api.deleteProject(projectId),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ['projects'] })
      navigate('/')
    },
  })

  return (
    <Shell>
      <Link to="/" className="text-sm text-[var(--muted)] hover:text-[var(--text)]">← Projects</Link>
      <div className="mt-3 mb-8 flex items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold mb-1">{project?.name ?? 'Project'}</h1>
          <p className="mono text-xs text-[var(--muted)]">{project?.repositoryUrl}</p>
        </div>
        <button
          type="button"
          disabled={!project || remove.isPending}
          onClick={() => {
            if (!confirm(`Delete “${project?.name ?? 'this project'}”? This also removes its tasks and workflows.`)) return
            remove.mutate()
          }}
          className="shrink-0 rounded border border-[var(--danger)] px-3 py-1.5 text-sm text-[var(--danger)] hover:bg-[var(--danger)]/10 disabled:opacity-40"
        >
          {remove.isPending ? 'Deleting…' : 'Delete'}
        </button>
      </div>
      {remove.isError && (
        <p className="mb-6 text-sm text-[var(--danger)]">{(remove.error as Error).message}</p>
      )}

      <section className="mb-8 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-5">
        <div className="mb-4 flex gap-2">
          <button
            type="button"
            onClick={() => setStartMode('jira')}
            className={`rounded px-3 py-1.5 text-sm ${
              startMode === 'jira'
                ? 'bg-[var(--accent)] text-[var(--bg)]'
                : 'border border-[var(--border)] text-[var(--muted)] hover:text-[var(--text)]'
            }`}
          >
            From Jira
          </button>
          <button
            type="button"
            onClick={() => setStartMode('manual')}
            className={`rounded px-3 py-1.5 text-sm ${
              startMode === 'manual'
                ? 'bg-[var(--accent)] text-[var(--bg)]'
                : 'border border-[var(--border)] text-[var(--muted)] hover:text-[var(--text)]'
            }`}
          >
            Write a task
          </button>
        </div>

        {startMode === 'jira' ? (
          <JiraPanel
            projectId={projectId}
            onStarted={(id) => {
              void qc.invalidateQueries({ queryKey: ['tasks', projectId] })
              navigate(`/workflows/${id}`)
            }}
          />
        ) : (
          <div className="grid gap-3">
            <input
              className="rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm"
              placeholder="Task name"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
            />
            <textarea
              className="rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm min-h-24"
              placeholder="Description / acceptance criteria"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
            />
            <div className="flex flex-wrap gap-2">
              <button
                type="button"
                disabled={!title || !description || startManual.isPending}
                onClick={() => startManual.mutate()}
                className="w-fit rounded bg-[var(--accent)] px-4 py-2 text-sm font-medium text-[var(--bg)] disabled:opacity-40"
              >
                {startManual.isPending ? 'Starting…' : 'Start workflow'}
              </button>
              <button
                type="button"
                disabled={!title || !description || createTask.isPending}
                onClick={() => createTask.mutate()}
                className="w-fit rounded border border-[var(--border)] px-4 py-2 text-sm disabled:opacity-40"
              >
                Save without running
              </button>
            </div>
            {startManual.isError && (
              <p className="text-sm text-[var(--danger)]">{(startManual.error as Error).message}</p>
            )}
            {createTask.isError && (
              <p className="text-sm text-[var(--danger)]">{(createTask.error as Error).message}</p>
            )}
          </div>
        )}
      </section>

      <h2 className="text-sm font-medium mb-3 uppercase tracking-wide text-[var(--muted)]">Tasks</h2>
      <ul className="space-y-3">
        {tasks.map((t) => (
          <li key={t.id} className="rounded-lg border border-[var(--border)] bg-[var(--surface)] px-4 py-3 flex items-start justify-between gap-4">
            <div>
              <div className="flex items-center gap-2">
                <div className="font-medium">{t.title}</div>
                {t.jiraIssueKey && (
                  t.jiraIssueUrl ? (
                    <a
                      href={t.jiraIssueUrl}
                      className="mono text-xs text-[var(--accent)]"
                      target="_blank"
                      rel="noreferrer"
                    >
                      {t.jiraIssueKey}
                    </a>
                  ) : (
                    <span className="mono text-xs text-[var(--accent)]">{t.jiraIssueKey}</span>
                  )
                )}
              </div>
              <div className="text-sm text-[var(--muted)] mt-1 line-clamp-2">{t.description}</div>
            </div>
            <button
              type="button"
              onClick={() => startWorkflow.mutate(t.id)}
              className="shrink-0 rounded border border-[var(--border)] px-3 py-1.5 text-sm hover:border-[var(--accent)]"
            >
              Run workflow
            </button>
          </li>
        ))}
      </ul>
    </Shell>
  )
}

function WorkflowsPage() {
  const { data: workflows = [], isLoading } = useQuery({
    queryKey: ['workflows'],
    queryFn: api.listWorkflows,
    refetchInterval: 3000,
  })

  return (
    <Shell>
      <h1 className="text-2xl font-semibold mb-6">Workflows</h1>
      {isLoading ? (
        <p className="text-[var(--muted)]">Loading…</p>
      ) : (
        <ul className="space-y-2">
          {workflows.map((w) => (
            <li key={w.id}>
              <Link
                to={`/workflows/${w.id}`}
                className="block rounded-lg border border-[var(--border)] bg-[var(--surface)] px-4 py-3 no-underline text-[var(--text)] hover:border-[var(--accent)]"
              >
                <div className="flex justify-between gap-4">
                  <span className="mono text-xs">{w.id.slice(0, 8)}…</span>
                  <span className="text-sm text-[var(--accent)]">{WORKFLOW_STATES[w.state] ?? w.state}</span>
                </div>
                {w.branchName && <div className="mono text-xs text-[var(--muted)] mt-1">{w.branchName}</div>}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Shell>
  )
}

function WorkflowDetailPage() {
  const { workflowId = '' } = useParams()
  const qc = useQueryClient()
  const [command, setCommand] = useState('')

  const { data, isLoading } = useQuery({
    queryKey: ['workflow', workflowId],
    queryFn: () => api.getWorkflow(workflowId),
    enabled: !!workflowId,
    refetchInterval: 2000,
  })

  const onEvent = useCallback(
    (_e: LiveEvent) => {
      void qc.invalidateQueries({ queryKey: ['workflow', workflowId] })
    },
    [qc, workflowId],
  )

  const live = useWorkflowHub(workflowId, onEvent)

  const cancel = useMutation({
    mutationFn: () => api.cancelWorkflow(workflowId),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['workflow', workflowId] }),
  })

  const rerun = useMutation({
    mutationFn: () => api.rerunWorkflow(workflowId),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['workflow', workflowId] }),
  })

  const sendCommand = useMutation({
    mutationFn: (text: string) => api.sendWorkflowCommand(workflowId, text),
    onSuccess: () => {
      setCommand('')
      void qc.invalidateQueries({ queryKey: ['workflow', workflowId] })
    },
  })

  const approve = useMutation({
    mutationFn: (approved: boolean) => api.approveWorkflow(workflowId, approved),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ['workflow', workflowId] }),
  })

  const w = data?.workflow
  const canRerun = w != null && w.state !== 9 && w.state !== 12

  return (
    <Shell wide>
      <Link to="/workflows" className="text-sm text-[var(--muted)] hover:text-[var(--text)]">← Workflows</Link>
      {isLoading || !w ? (
        <p className="mt-4 text-[var(--muted)]">Loading…</p>
      ) : (
        <>
          <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
            <h1 className="text-2xl font-semibold">Workflow</h1>
            <div className="flex items-center gap-3 text-sm">
              <span className={live ? 'text-[var(--accent)]' : 'text-[var(--muted)]'}>
                {live ? 'Live' : 'Polling'}
              </span>
              {w.state === 9 && (
                <>
                  <button
                    type="button"
                    onClick={() => approve.mutate(true)}
                    className="rounded bg-[var(--accent)] px-3 py-1 text-[var(--bg)]"
                  >
                    Approve MR
                  </button>
                  <button
                    type="button"
                    onClick={() => approve.mutate(false)}
                    className="rounded border border-[var(--danger)] px-3 py-1 text-[var(--danger)]"
                  >
                    Reject
                  </button>
                </>
              )}
              {canRerun && (
                <button
                  type="button"
                  disabled={rerun.isPending}
                  onClick={() => rerun.mutate()}
                  className="rounded bg-[var(--accent)] px-3 py-1 text-[var(--bg)] disabled:opacity-40"
                >
                  {rerun.isPending ? 'Re-running…' : 'Re-run'}
                </button>
              )}
              {w.state < 10 && (
                <button
                  type="button"
                  onClick={() => cancel.mutate()}
                  className="rounded border border-[var(--danger)] px-3 py-1 text-[var(--danger)]"
                >
                  Cancel
                </button>
              )}
            </div>
          </div>

          {rerun.isError && (
            <p className="mt-3 text-sm text-[var(--danger)]">{(rerun.error as Error).message}</p>
          )}
          {sendCommand.isError && (
            <p className="mt-3 text-sm text-[var(--danger)]">{(sendCommand.error as Error).message}</p>
          )}

          <WorkflowDiagram steps={data.steps ?? []} />

          <div className="mt-6 grid gap-4 sm:grid-cols-2">
            <Info label="State" value={WORKFLOW_STATES[w.state] ?? String(w.state)} />
            <Info label="Correlation" value={w.correlationId} mono />
            <Info label="Branch" value={w.branchName ?? '—'} mono />
            <Info label="Commit" value={w.commitSha ?? '—'} mono />
          </div>

          {data.mergeRequest && (
            <div className="mt-6 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-4">
              <div className="text-sm text-[var(--muted)] uppercase tracking-wide mb-2">Merge request</div>
              <div className="font-medium">{data.mergeRequest.title}</div>
              {data.mergeRequest.url && (
                <a href={data.mergeRequest.url} className="mono text-xs text-[var(--accent)] break-all" target="_blank" rel="noreferrer">
                  {data.mergeRequest.url}
                </a>
              )}
            </div>
          )}

          {data.testRuns.length > 0 && (
            <div className="mt-6 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-4">
              <div className="text-sm text-[var(--muted)] uppercase tracking-wide mb-2">Build / tests</div>
              <ul className="space-y-2 text-sm">
                {data.testRuns.map((t) => (
                  <li key={t.id} className="flex justify-between gap-4">
                    <span>{t.success ? 'Passed' : 'Failed'}</span>
                    <span className="mono text-xs text-[var(--muted)]">
                      build {t.buildPassed ? 'ok' : 'fail'} · {t.passed} passed · {t.failed} failed
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          <section className="mt-6 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-4">
            <div className="text-sm text-[var(--muted)] uppercase tracking-wide mb-2">Add to development</div>
            <p className="text-sm text-[var(--muted)] mb-3">
              Send extra instructions now. They are appended to this workflow and the development step runs immediately.
            </p>
            <textarea
              className="w-full rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm min-h-24"
              placeholder="e.g. Also add request logging to the health endpoint"
              value={command}
              onChange={(e) => setCommand(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  const text = command.trim()
                  if (text && !sendCommand.isPending) sendCommand.mutate(text)
                }
              }}
            />
            <div className="mt-3 flex items-center justify-between gap-3">
              <span className="text-xs text-[var(--muted)]">Enter to send · Shift+Enter for a new line</span>
              <button
                type="button"
                disabled={!command.trim() || sendCommand.isPending}
                onClick={() => sendCommand.mutate(command.trim())}
                className="rounded bg-[var(--accent)] px-4 py-1.5 text-sm font-medium text-[var(--bg)] disabled:opacity-40"
              >
                {sendCommand.isPending ? 'Sending…' : 'Send'}
              </button>
            </div>
          </section>

          <h2 className="mt-8 mb-3 text-sm font-medium uppercase tracking-wide text-[var(--muted)]">Timeline</h2>
          <ol className="space-y-2 border-l border-[var(--border)] pl-4">
            {data.events.map((e) => (
              <li key={e.id} className="relative">
                <span className="absolute -left-[1.3rem] top-1.5 h-2 w-2 rounded-full bg-[var(--accent)]" />
                <div className="text-sm font-medium">{e.eventType}</div>
                {e.message && <div className="text-sm text-[var(--muted)]">{e.message}</div>}
                <div className="mono text-[10px] text-[var(--muted)] mt-0.5">
                  {new Date(e.occurredAt).toLocaleString()}
                </div>
              </li>
            ))}
          </ol>
        </>
      )}
    </Shell>
  )
}

function Info({ label, value, mono }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border border-[var(--border)] bg-[var(--surface)] px-4 py-3">
      <div className="text-xs uppercase tracking-wide text-[var(--muted)]">{label}</div>
      <div className={`mt-1 text-sm ${mono ? 'mono break-all' : ''}`}>{value}</div>
    </div>
  )
}

export default function App() {
  return (
    <Routes>
      <Route path="/" element={<ProjectsPage />} />
      <Route path="/projects/:projectId" element={<ProjectDetailPage />} />
      <Route path="/workflows" element={<WorkflowsPage />} />
      <Route path="/workflows/:workflowId" element={<WorkflowDetailPage />} />
    </Routes>
  )
}
