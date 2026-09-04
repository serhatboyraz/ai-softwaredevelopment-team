import { Fragment, useEffect, useMemo, useState } from 'react'
import type { WorkflowStepProgress } from './api'

const MAIN_KEYS = ['context', 'analysis', 'development', 'testing', 'git', 'completed'] as const

const STATUS_LABEL: Record<WorkflowStepProgress['status'], string> = {
  pending: 'Pending',
  running: 'Working',
  waiting: 'Waiting',
  completed: 'Done',
  failed: 'Failed',
  skipped: 'Skipped',
}

type Props = {
  steps: WorkflowStepProgress[]
}

export function WorkflowDiagram({ steps }: Props) {
  const [now, setNow] = useState(() => Date.now())
  const live = steps.some((s) => s.status === 'running' || s.status === 'waiting')

  useEffect(() => {
    if (!live) return
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [live])

  const byKey = useMemo(() => new Map(steps.map((s) => [s.key, s])), [steps])
  const main = MAIN_KEYS.map((key) => byKey.get(key)).filter((s): s is WorkflowStepProgress => !!s)
  const fixing = byKey.get('fixing')

  const totalTokens = steps.reduce((sum, s) => sum + s.totalTokens, 0)
  const totalMs = steps.reduce((sum, s) => sum + liveDurationMs(s, now), 0)

  if (main.length === 0) return null

  return (
    <section className="mt-6 rounded-lg border border-[var(--border)] bg-[var(--surface)] p-4">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <div className="text-sm text-[var(--muted)] uppercase tracking-wide">Workflow</div>
          <p className="text-xs text-[var(--muted)] mt-1">Each box is a step. The working step is highlighted.</p>
        </div>
        <div className="flex gap-4 text-sm">
          <span className="mono">
            {formatDuration(totalMs)}
          </span>
          <span className="mono text-[var(--accent)]">
            {formatTokens(totalTokens)}
          </span>
        </div>
      </div>

      <div className="overflow-x-auto pb-2">
        <div className="min-w-[980px]">
          <div className="flex items-stretch">
            {main.map((step, index) => (
              <Fragment key={step.key}>
                {index > 0 && (
                  <FlowArrow lit={isLit(main[index - 1]) || step.status === 'running' || step.status === 'waiting'} />
                )}
                <div className="min-w-0 flex-1">
                  <StepBox step={step} now={now} />
                </div>
              </Fragment>
            ))}
          </div>

          {fixing && (
            <div className="mt-1 flex items-start">
              <div className="flex-1" />
              <div className="w-8 shrink-0" />
              <div className="flex-1" />
              <div className="w-8 shrink-0" />
              <div className="flex-[2.15] flex min-w-0 flex-col items-center px-2">
                <LoopMarks active={fixing.status === 'running' || fixing.status === 'completed' || fixing.status === 'failed'} />
                <div className="w-full max-w-[240px]">
                  <StepBox step={fixing} now={now} compact />
                </div>
              </div>
              <div className="w-8 shrink-0" />
              <div className="flex-1" />
              <div className="w-8 shrink-0" />
              <div className="flex-1" />
            </div>
          )}
        </div>
      </div>

      <ul className="mt-4 flex flex-wrap gap-3 text-[11px] text-[var(--muted)]">
        <Legend swatch="var(--warn)" label="Working" />
        <Legend swatch="var(--info)" label="Waiting" />
        <Legend swatch="var(--accent)" label="Done" />
        <Legend swatch="var(--danger)" label="Failed" />
        <Legend swatch="var(--border)" label="Pending" />
      </ul>
    </section>
  )
}

function StepBox({
  step,
  now,
  compact = false,
}: {
  step: WorkflowStepProgress
  now: number
  compact?: boolean
}) {
  const duration = liveDurationMs(step, now)
  const showTokens = step.totalTokens > 0 || step.status === 'completed' || step.status === 'running'

  return (
    <div
      className={`h-full rounded-lg border px-3 py-3 transition-colors ${boxClass(step.status)}`}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="font-medium text-sm leading-tight">{step.label}</div>
        <span className={`shrink-0 rounded-full px-2 py-0.5 text-[10px] uppercase tracking-wide ${badgeClass(step.status)}`}>
          {STATUS_LABEL[step.status]}
        </span>
      </div>
      <div className="mt-3 space-y-1 text-xs">
        <div className="flex justify-between gap-2">
          <span className="text-[var(--muted)]">Time</span>
          <span className="mono">{duration > 0 || step.status === 'running' ? formatDuration(duration) : '—'}</span>
        </div>
        <div className="flex justify-between gap-2">
          <span className="text-[var(--muted)]">Tokens</span>
          <span className="mono">{showTokens ? formatTokens(step.totalTokens) : '—'}</span>
        </div>
        {(step.promptTokens > 0 || step.completionTokens > 0) && (
          <div className="flex justify-between gap-2 text-[10px] text-[var(--muted)]">
            <span>in {formatCount(step.promptTokens)}</span>
            <span>out {formatCount(step.completionTokens)}</span>
          </div>
        )}
      </div>
      {!compact && step.summary && (
        <p className="mt-2 line-clamp-2 text-[11px] text-[var(--muted)]">{step.summary}</p>
      )}
    </div>
  )
}

function FlowArrow({ lit }: { lit: boolean }) {
  return (
    <div className={`flex w-8 shrink-0 items-center justify-center ${lit ? 'text-[var(--accent)]' : 'text-[var(--border)]'}`}>
      <svg viewBox="0 0 32 16" className="h-4 w-8" aria-hidden>
        <line x1="0" y1="8" x2="22" y2="8" stroke="currentColor" strokeWidth="2" />
        <polygon points="22,3 32,8 22,13" fill="currentColor" />
      </svg>
    </div>
  )
}

function LoopMarks({ active }: { active: boolean }) {
  const color = active ? 'var(--warn)' : 'var(--border)'
  return (
    <div className="mb-1 flex h-8 w-full items-start justify-center text-[10px] uppercase tracking-wide" style={{ color }}>
      <svg viewBox="0 0 220 28" className="h-7 w-full max-w-[280px]" aria-hidden>
        <path d="M40 2 v10 h140 v-10" fill="none" stroke="currentColor" strokeWidth="2" />
        <polygon points="36,12 40,2 44,12" fill="currentColor" />
        <polygon points="176,12 180,2 184,12" fill="currentColor" />
      </svg>
    </div>
  )
}

function Legend({ swatch, label }: { swatch: string; label: string }) {
  return (
    <li className="flex items-center gap-1.5">
      <span className="h-2 w-2 rounded-full" style={{ background: swatch }} />
      {label}
    </li>
  )
}

function isLit(step?: WorkflowStepProgress) {
  return step?.status === 'completed' || step?.status === 'running' || step?.status === 'waiting'
}

function liveDurationMs(step: WorkflowStepProgress, now: number) {
  if ((step.status === 'running' || step.status === 'waiting') && step.startedAt) {
    const started = new Date(step.startedAt).getTime()
    if (!Number.isNaN(started) && now >= started)
      return Math.max(step.durationMs, now - started)
  }
  return step.durationMs
}

function boxClass(status: WorkflowStepProgress['status']) {
  switch (status) {
    case 'running':
      return 'wf-running border-[var(--warn)] bg-[#2a2316]'
    case 'waiting':
      return 'border-[var(--info)] bg-[#16202c]'
    case 'completed':
      return 'border-[var(--accent)] bg-[#16241f]'
    case 'failed':
      return 'border-[var(--danger)] bg-[#2a1818]'
    case 'skipped':
      return 'border-dashed border-[var(--border)] bg-[var(--bg)] opacity-55'
    default:
      return 'border-[var(--border)] bg-[var(--bg)]'
  }
}

function badgeClass(status: WorkflowStepProgress['status']) {
  switch (status) {
    case 'running':
      return 'bg-[var(--warn)] text-[var(--bg)]'
    case 'waiting':
      return 'bg-[var(--info)] text-[var(--bg)]'
    case 'completed':
      return 'bg-[var(--accent)]/20 text-[var(--accent)]'
    case 'failed':
      return 'bg-[var(--danger)]/20 text-[var(--danger)]'
    default:
      return 'bg-[var(--border)] text-[var(--muted)]'
  }
}

export function formatDuration(ms: number) {
  if (ms <= 0) return '0s'
  if (ms < 1000) return '<1s'
  const total = Math.floor(ms / 1000)
  const hours = Math.floor(total / 3600)
  const minutes = Math.floor((total % 3600) / 60)
  const seconds = total % 60
  if (hours > 0) return `${hours}h ${minutes}m`
  if (minutes > 0) return `${minutes}m ${seconds.toString().padStart(2, '0')}s`
  return `${seconds}s`
}

export function formatTokens(n: number) {
  if (n <= 0) return '0 tokens'
  return `${formatCount(n)} tokens`
}

function formatCount(n: number) {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`
  if (n >= 10_000) return `${(n / 1000).toFixed(1)}k`
  return n.toLocaleString()
}
