---
name: create-project-documentation
description: Create or refresh a complete software-project documentation suite: ADRs, user guide, administrator guide, changelog, and versioned release notes. Use when a project needs launch documentation, a documentation audit, release preparation, or consistent operational and user-facing docs.
---

# Create Project Documentation

Use this skill to turn an implemented project into a coherent, fact-checked documentation set. It works for applications, libraries, services, CLIs, platforms, and monorepos. Document implemented behavior separately from roadmap or aspirational behavior.

## Process

1. **Inventory the project.** Inspect the existing README and docs, source and deployment layout, configuration and environment examples, package/project manifests, test/build scripts, version files, tags, and recent history. Identify user personas, operators, deployment targets, security boundaries, and supported integrations.
2. **Resolve scope and version.** Prefer an existing tag or package/application version. If versions disagree, report the conflict and use the authoritative release source; do not silently invent a version. If no release exists, use `Unreleased` unless the requester supplies a version. Separate current behavior, known limitations, and planned work.
3. **Create the document set.** Use the templates in `references/` as a starting point. Create only applicable sections, but do not omit a requested artifact. For a project with no end users or administrators, state that the role is not applicable and document the relevant developer/operator workflow instead.
4. **Write ADRs from evidence.** Record durable architectural decisions, not every implementation detail. Each ADR must include status, date, context, decision, alternatives, consequences, and implementation references. Link the ADR index from the README.
5. **Write task-oriented guides.** The user guide should describe goals, workflows, visible states, recovery, accessibility, and privacy. The administrator guide should describe prerequisites, installation, configuration, secrets, operations, backups, upgrades, troubleshooting, and security hardening. Use exact commands and configuration names found in the project.
6. **Prepare release communication.** Add a Keep a Changelog-compatible changelog and release notes for the resolved version. Include highlights, compatibility, installation/upgrade notes, known limitations, validation status, and links to detailed docs. Do not claim tests, integrations, or production support that were not verified.
7. **Refresh navigation and stale docs.** Correct stale status statements in component docs and add links to canonical guides and release notes. Preserve historical requirements and design documents; label their status rather than rewriting history.
8. **Validate.** Check local Markdown links, headings and navigation, commands against scripts, configuration names against source, version references, secret leakage, unsupported claims, and spelling of product terms. Run the project’s available build/test/lint gates and `git diff --check`.

## Required Evidence Rules

- Prefer source code, manifests, deployment files, tests, and observed command output over requirements documents when describing current behavior.
- Treat unchecked task lists and roadmap statements as plans unless implementation evidence confirms completion.
- Never publish passwords, tokens, private URLs, signing keys, or copied secret values. Show variable names and safe placeholders only.
- Explain delivery semantics precisely. “At least once with idempotent effect” is not the same as transport-level exactly once.
- Mark consumer-owned responsibilities, such as workflow authorization or audit, instead of assigning them to this project.

## Outputs

Default paths are `CHANGELOG.md`, `docs/adr/`, `docs/user-guide.md`, `docs/admin-guide.md`, and `docs/releases/`. Adapt paths to the repository’s existing convention when one exists. Keep `SKILL.md` generic; project facts belong in generated documentation.

Load these references when writing the corresponding artifact:

- Load `references/adr-template.md` when creating or updating an ADR.
- Load `references/user-guide-template.md` when the project has end users or a client workflow.
- Load `references/admin-guide-template.md` when the project is deployed, hosted, configured, or operated.
- Load `references/changelog-template.md` when establishing or refreshing change history.
- Load `references/release-notes-template.md` when preparing a versioned release.

## Validation Checklist

- [ ] Requested documents exist and are linked from the README or documentation index.
- [ ] Version and release date agree with the authoritative version source.
- [ ] No credentials or secrets are present.
- [ ] Commands, ports, paths, environment variables, and endpoints match the repository.
- [ ] Current, planned, and unsupported behavior are clearly distinguished.
- [ ] ADRs have stable identifiers and complete decision sections.
- [ ] User and administrator audiences are clearly separated.
- [ ] Release notes link to upgrade, user, and administrator guidance.
- [ ] Local Markdown links resolve, including links from nested docs.
- [ ] Build, test, lint, and documentation checks have been run where available.

## Gotchas

**Stale implementation status.** Feature documents often retain unchecked planning tasks after code lands. Verify against source, tests, and progress records, then label historical checklists instead of presenting them as current truth.

**Version drift.** Monorepos commonly contain several package versions and an app version. Identify which version defines the release, document other package versions as component versions, and do not mass-replace unrelated dependency versions.

**Deployment-only settings.** A variable shown in a Compose file may be development-only or intentionally unsafe outside a trusted network. Explain its scope and never present insecure development flags as production instructions.

**False integration claims.** A contract stub, mock, or provider test does not prove compatibility with a live external product. Name the validation boundary and the untested external runtime explicitly.

**Broken nested links.** Relative links are resolved from the linking file, not the repository root. Validate links after moving content and use paths relative to each document.
