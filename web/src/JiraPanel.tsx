import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { api, type JiraIssueDetails, type JiraIssueSummary } from './api'

type Props = {
  projectId: string
  onStarted: (workflowId: string) => void
}

export function JiraPanel({ projectId, onStarted }: Props) {
  const status = useQuery({ queryKey: ['jira', 'status'], queryFn: api.jiraStatus })
  const boards = useQuery({
    queryKey: ['jira', 'boards'],
    queryFn: api.jiraBoards,
    enabled: status.data?.configured === true,
  })

  const [boardId, setBoardId] = useState<number | ''>('')
  const [selectedKey, setSelectedKey] = useState<string | null>(null)

  useEffect(() => {
    if (boardId !== '' || !status.data?.configured) return
    const preferred = status.data.defaultBoardId
    if (preferred) {
      setBoardId(preferred)
      return
    }
    if (boards.data && boards.data.length > 0)
      setBoardId(boards.data[0].id)
  }, [boardId, status.data, boards.data])

  const work = useQuery({
    queryKey: ['jira', 'work', boardId],
    queryFn: () => api.jiraBoardWork(boardId as number),
    enabled: status.data?.configured === true && boardId !== '',
  })

  const details = useQuery({
    queryKey: ['jira', 'issue', selectedKey],
    queryFn: () => api.jiraIssue(selectedKey!),
    enabled: !!selectedKey,
  })

  const start = useMutation({
    mutationFn: (jiraIssueKey: string) => api.startProjectWorkflow(projectId, { jiraIssueKey }),
    onSuccess: (wf) => onStarted(wf.id),
  })

  const activeIssues = useMemo(() => {
    if (!work.data) return []
    if (work.data.activeSprint) return work.data.activeSprint.issues
    return work.data.boardIssues
  }, [work.data])

  const activeTitle = work.data?.activeSprint
    ? `Active board · ${work.data.activeSprint.name}`
    : 'Active board'

  if (status.isLoading) {
    return <p className="text-sm text-[var(--muted)]">Checking Jira connection…</p>
  }

  if (!status.data?.configured) {
    return (
      <div className="text-sm text-[var(--muted)] space-y-2">
        <p>{status.data?.message ?? 'Jira is not configured.'}</p>
        <p>
          In <span className="mono">appsettings.json</span> set{' '}
          <span className="mono">Jira:BaseUrl</span>,{' '}
          <span className="mono">Jira:Email</span> (Cloud), and{' '}
          <span className="mono">Jira:Token</span>. Optionally set{' '}
          <span className="mono">Jira:BoardId</span>.
        </p>
      </div>
    )
  }

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-end gap-3">
        <label className="grid gap-1 text-sm min-w-56 flex-1">
          <span className="text-xs uppercase tracking-wide text-[var(--muted)]">Board</span>
          <select
            className="rounded border border-[var(--border)] bg-[var(--bg)] px-3 py-2 text-sm"
            value={boardId}
            onChange={(e) => {
              setSelectedKey(null)
              setBoardId(e.target.value === '' ? '' : Number(e.target.value))
            }}
          >
            <option value="">Select a board</option>
            {(boards.data ?? []).map((b) => (
              <option key={b.id} value={b.id}>
                {b.name}
                {b.projectKey ? ` (${b.projectKey})` : ''}
              </option>
            ))}
          </select>
        </label>
        {work.data && (
          <span className="text-xs text-[var(--muted)] pb-2">{work.data.type} board</span>
        )}
      </div>

      {boards.isError && (
        <p className="text-sm text-[var(--danger)]">{(boards.error as Error).message}</p>
      )}
      {work.isError && (
        <p className="text-sm text-[var(--danger)]">{(work.error as Error).message}</p>
      )}

      {work.isLoading ? (
        <p className="text-sm text-[var(--muted)]">Loading issues…</p>
      ) : work.data ? (
        <div className="grid gap-4 sm:grid-cols-2">
          <IssueColumn
            title={activeTitle}
            issues={activeIssues}
            selectedKey={selectedKey}
            onSelect={setSelectedKey}
          />
          <IssueColumn
            title="Backlog"
            issues={work.data.backlog}
            selectedKey={selectedKey}
            onSelect={setSelectedKey}
          />
        </div>
      ) : null}

      {selectedKey && (
        <IssuePreview
          issueKey={selectedKey}
          details={details.data}
          loading={details.isLoading}
          error={details.isError ? (details.error as Error).message : null}
          starting={start.isPending}
          startError={start.isError ? (start.error as Error).message : null}
          onStart={() => start.mutate(selectedKey)}
        />
      )}
    </div>
  )
}

function IssueColumn({
  title,
  issues,
  selectedKey,
  onSelect,
}: {
  title: string
  issues: JiraIssueSummary[]
  selectedKey: string | null
  onSelect: (key: string) => void
}) {
  return (
    <div className="rounded-lg border border-[var(--border)] bg-[var(--bg)] p-3 min-h-48">
      <div className="flex items-center justify-between mb-2">
        <h3 className="text-xs font-medium uppercase tracking-wide text-[var(--muted)]">{title}</h3>
        <span className="text-xs text-[var(--muted)]">{issues.length}</span>
      </div>
      {issues.length === 0 ? (
        <p className="text-sm text-[var(--muted)]">No issues.</p>
      ) : (
        <ul className="space-y-1 max-h-80 overflow-auto pr-1">
          {issues.map((issue) => {
            const selected = issue.key === selectedKey
            return (
              <li key={issue.key}>
                <button
                  type="button"
                  onClick={() => onSelect(issue.key)}
                  className={`w-full text-left rounded px-2 py-2 text-sm border ${
                    selected
                      ? 'border-[var(--accent)] bg-[var(--accent)]/10'
                      : 'border-transparent hover:border-[var(--border)] hover:bg-[var(--surface)]'
                  }`}
                >
                  <div className="flex items-center gap-2">
                    <span className="mono text-xs text-[var(--accent)]">{issue.key}</span>
                    <span className="text-[10px] uppercase tracking-wide text-[var(--muted)]">
                      {issue.issueType}
                    </span>
                    <span className="ml-auto text-[10px] text-[var(--muted)]">{issue.status}</span>
                  </div>
                  <div className="mt-0.5 line-clamp-2">{issue.summary}</div>
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </div>
  )
}

function IssuePreview({
  issueKey,
  details,
  loading,
  error,
  starting,
  startError,
  onStart,
}: {
  issueKey: string
  details?: JiraIssueDetails
  loading: boolean
  error: string | null
  starting: boolean
  startError: string | null
  onStart: () => void
}) {
  return (
    <div className="rounded-lg border border-[var(--border)] bg-[var(--bg)] p-4">
      <div className="flex flex-wrap items-start justify-between gap-3 mb-3">
        <div>
          <div className="mono text-xs text-[var(--accent)]">{issueKey}</div>
          <div className="font-medium">{details?.summary ?? (loading ? 'Loading…' : issueKey)}</div>
          {details && (
            <div className="mt-1 text-xs text-[var(--muted)]">
              {details.issueType}
              {details.status ? ` · ${details.status}` : ''}
              {details.priority ? ` · ${details.priority}` : ''}
              {details.assignee ? ` · ${details.assignee}` : ''}
            </div>
          )}
        </div>
        <button
          type="button"
          disabled={starting || loading}
          onClick={onStart}
          className="shrink-0 rounded bg-[var(--accent)] px-4 py-2 text-sm font-medium text-[var(--bg)] disabled:opacity-40"
        >
          {starting ? 'Starting…' : 'Start workflow'}
        </button>
      </div>
      {details?.url && (
        <a
          href={details.url}
          className="mono text-xs text-[var(--accent)]"
          target="_blank"
          rel="noreferrer"
        >
          Open in Jira
        </a>
      )}
      {error && <p className="mt-2 text-sm text-[var(--danger)]">{error}</p>}
      {startError && <p className="mt-2 text-sm text-[var(--danger)]">{startError}</p>}
      {details && (
        <pre className="mt-3 whitespace-pre-wrap text-sm text-[var(--muted)] max-h-64 overflow-auto">
          {details.formattedDescription}
        </pre>
      )}
    </div>
  )
}
