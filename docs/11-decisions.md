# Architectural Decisions

| # | Decision | Reason |
|---|----------|--------|
| 1 | Azure OpenAI (not Foundry Agent Service) | Own runtime; simpler MVP; local dev; provider abstraction |
| 2 | Microsoft Agent Framework | .NET fit; typed workflows; checkpointing; HITL; observability |
| 3 | Docker for code execution | Repository code is untrusted |
| 4 | PostgreSQL for workflow state | Survive restarts; authoritative store |
| 5 | SignalR for live UI | Real-time UX; DB remains source of truth |
| 6 | `IChatClient` / AI abstraction | Provider changes without rewriting agents |

## Future providers

Azure OpenAI → OpenAI / Claude / Gemini / Ollama / others without changing workflow logic.
