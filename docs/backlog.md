# Product Backlog

This backlog records work intentionally deferred from HumanGateway `0.1.0`. Items below are future layers, not current release capabilities.

## Setup CLI Follow-Up

- Publish the repository CLI as `npx @humangateway/cli setup` after the local CLI is stable.
- Add clean-machine CI and non-interactive setup examples.
- Add platform-specific prerequisite guidance or installers for Linux, Windows, macOS, Raspberry Pi, Docker, and Podman.
- Add sanitized, telemetry-free diagnostic export for support cases.
- Add `doctor`, `start`, `stop`, `restart`, `logs`, `backup`, `upgrade`, and registration-token rotation commands.
- Add explicit setup support for Windows services and systemd.

## Production Deployment Layer

- Add production TLS certificate provisioning, renewal, and reverse-proxy integration.
- Add secret-store integrations and production-safe credential rotation.
- Add non-Compose production packaging and environment validation.
- Add migration safeguards, pre-upgrade checks, rollback checks, and release compatibility checks.
- Automate encrypted Edge and Relay backups, restore verification, and retention policies.
- Add storage capacity, artifact quota, health, and sync-failure monitoring with alert integrations.
- Add log retention and content/token redaction configuration.
- Add production firewall and network-hardening checks.
- Add multi-gateway per-site failover and recovery workflows.
- Add external object storage for high-scale or archival artifacts.
- Perform a formal production-readiness review and publish a supported deployment matrix.

## CLI Expansion

- Add `setup:export` for sanitized configuration diagnostics.
- Add a dry-run mode that prints planned non-secret actions.
- Add upgrade detection for existing installations and data migrations.
- Add automated gateway registration status and token-rotation status reporting.
- Add optional smoke-test message/task flows after setup.
