# MVP

## End-to-end path

1. User creates project (GitLab repo)
2. User creates task
3. Workflow starts
4. Context Agent checks requirements
5. Analysis Agent creates plan
6. Development Agent changes code
7. Docker builds + tests
8. Development Agent fixes failures if needed (≤3)
9. Final diff reviewed
10. Branch / commit / push / GitLab MR
11. Workflow completes; frontend shows live transitions

## MVP architecture

```text
React → ASP.NET Core API → BackgroundService → Agent Framework → Azure OpenAI
                              ├── PostgreSQL
                              ├── GitLab
                              ├── Docker
                              ├── Key Vault (or env secrets locally)
                              └── SignalR
```

One Agent Framework workflow can contain all five agent roles. No need for separate agent services.

## Local development

`docker-compose.yml`: api, worker (or combined), postgres, sandbox. Azure OpenAI remains external. Secrets via env / user-secrets — never committed.

## Configuration categories

`AI`, `Database`, `GitLab`, `Docker`, `KeyVault`, `SignalR`, `ServiceBus`, `Observability`
