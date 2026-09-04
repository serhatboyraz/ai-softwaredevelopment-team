# Frontend — Workflow Dashboard

Focus: execution visibility, not chat.

## Sections

- Task, current state, progress, timeline
- Agent activity, tool activity
- Build/test results
- Changed files, git branch, commit, MR
- Approval UI

## Safe summaries only

Do not expose chain-of-thought. Show summaries like:

- Context: "Repository context is sufficient."
- Analysis: "Identified 4 affected files."
- Development: "Modified authentication service."
- Testing: "3 tests failed."
- Git: "Created branch and pushed commit."

## Stack

React, TypeScript, Vite, TanStack Query, SignalR client, Tailwind CSS.
