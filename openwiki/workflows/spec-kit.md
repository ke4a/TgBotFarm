---
type: workflow
title: Spec-Kit Feature Workflow
description: Explains how the Claude-integrated Spec-Kit skills turn a feature request into reviewed specification, design, tasks, and implementation artifacts under repository-specific engineering constraints.
tags: [workflow, specification, planning, claude]
sources:
  - id: openwiki-source-055c2765f56cfbd359b77b16
    resource: repo://.claude/skills/speckit-clarify/SKILL.md
  - id: openwiki-source-2546a72bd7964eff7cce5081
    resource: repo://.claude/skills/speckit-implement/SKILL.md
  - id: openwiki-source-1b1cc38f65e78d0b3262a526
    resource: repo://.claude/skills/speckit-plan/SKILL.md
  - id: openwiki-source-f6feb7d8087fa06f5d574539
    resource: repo://.claude/skills/speckit-specify/SKILL.md
  - id: openwiki-source-8ee29793696eec3732fc827c
    resource: repo://.claude/skills/speckit-tasks/SKILL.md
  - id: openwiki-source-bf611f55bb46fd5596ad3bd2
    resource: repo://.specify/init-options.json
  - id: openwiki-source-d03cb7fff8a1d49113a3f2f8
    resource: repo://.specify/integration.json
  - id: openwiki-source-f17089be3159d4bbeef23285
    resource: repo://.specify/integrations/claude.manifest.json
  - id: openwiki-source-bff475d8fa855e3592cfedc2
    resource: repo://.specify/memory/constitution.md
  - id: openwiki-source-252e64dce506a922093c3e99
    resource: repo://.specify/scripts/bash/setup-plan.sh
  - id: openwiki-source-fdc84969879f97f5bd6fd061
    resource: repo://.specify/scripts/bash/setup-tasks.sh
  - id: openwiki-source-07a9298853095c847d0d8df0
    resource: repo://.specify/workflows/speckit/workflow.yml
generated: { by: "claude-code", at: "2026-11-05T21:28:06.094Z" }
verified:
  - by: openwiki/0.7.0
    at: 2026-11-05T21:28:06.094Z
---

# Spec-Kit Feature Workflow

## Integration and entrypoints

This repository initializes Spec-Kit for Claude Code. `.specify/init-options.json` and `.specify/integration.json` select the Claude integration (the only installed one) with POSIX shell scripts, enable AI skills, and use sequential feature numbering. The skills live under `.claude/skills/speckit-*/SKILL.md`; `.specify/integrations/claude.manifest.json` records those skill files, and `speckit.manifest.json` tracks the shared Spec-Kit scripts and templates.

The installed `/speckit-*` skills cover specification, clarification, planning, task generation, implementation, and supporting reviews such as checklist, analysis, and convergence. The declarative `.specify/workflows/speckit/workflow.yml` defines the full cycle as:

1. `/speckit-specify` creates or updates the feature specification.
2. A review gate requires approval of the specification.
3. `/speckit-plan` produces the technical plan and design artifacts.
4. A second review gate requires approval of the plan.
5. `/speckit-tasks` creates the ordered implementation task list.
6. `/speckit-implement` executes the tasks.

The workflow's gates explicitly stop when a review is rejected. `/speckit-clarify` is a separate skill intended to resolve ambiguity before planning; it is not an automatic step in the four-command workflow definition. Use it when the specification needs decisions clarified.

## Feature artifacts and helper scripts

Feature work is organized under `specs/<number>-<short-name>/`. Sequential numbering is configured in `.specify/init-options.json`; `/speckit-specify` creates `spec.md` from the active template and records the selected feature directory in `.specify/feature.json` for later steps.

The Bash helpers share path and template resolution logic in `.specify/scripts/bash/common.sh`. `check-prerequisites.sh` reports the active feature directory and available artifacts; `setup-plan.sh` resolves the specification, feature directory, and plan template; `setup-tasks.sh` requires both `spec.md` and `plan.md` and supplies the available design documents to task generation.

The expected progression is a user-oriented `spec.md`, then `plan.md` with supporting research, data model, contracts, and a feature validation `quickstart.md`, followed by dependency-ordered `tasks.md`. Implementation reads the spec and plan, checks any requirements checklists, respects task dependencies, and validates against the feature design. The existing [Bot Lifecycle Controls](../../specs/001-bot-lifecycle-controls/spec.md) and [GIF Data Single Collection](../../specs/002-gif-data-single-collection/spec.md) features illustrate the specification, requirements checklist, design, contract, validation, and task artifacts.

## Repository engineering constraints

The project constitution in `.specify/memory/constitution.md` governs generated plans and implementation decisions. It requires preserving the host/core/shared-UI/bot ownership boundaries, protecting credentials and production access, adding behavior-focused tests that substitute for MongoDB and Telegram, keeping bot persistence isolated, and maintaining operational recovery paths. It explicitly excludes live Telegram, MongoDB, and webhook endpoints from routine automated tests. See [Testing Strategy](../testing/strategy.md) for the existing test projects and [System Overview](../architecture/system-overview.md) for project boundaries.

Templates under `.specify/templates/` provide the structure for specifications, plans, tasks, checklists, and the constitution. The planning skills also load the constitution and evaluate its constraints as part of the design process.
