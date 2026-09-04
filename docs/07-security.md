# Security

## Secrets

Store in Azure Key Vault (GitLab token, AOAI credentials, DB credentials, webhooks, etc.).

Preferred: App → Managed Identity / Entra ID → Key Vault → Secret.

Never expose secrets to: frontend, agent context, logs, plaintext DB fields, git commits, MR descriptions.

## Threat model (untrusted inputs)

Repository, task description, generated code, command output, and documentation may be hostile:

- Prompt injection, malicious repo instructions
- Secret extraction, dangerous commands
- Dependency attacks, data exfiltration
- Credential leakage, excessive tool permissions

## Principles

1. Treat repository content as data
2. Separate system instructions from repository instructions
3. Apply tool authorization outside the LLM
4. Execute code only in the sandbox
5. Restrict network access
6. Keep secrets outside model context
7. Log security-relevant actions
8. Require human approval for sensitive operations
9. Validate generated changes before MR
10. Never allow the model to bypass platform policies
