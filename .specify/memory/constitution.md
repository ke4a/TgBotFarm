<!--
Sync Impact Report
Version change: uninitialized → 1.0.0 (initial ratification)
Modified principles:
  Principle 1 → I. Preserve Layered Ownership
  Principle 2 → II. Secure Configuration and Access
  Principle 3 → III. Behavior-Focused Automated Tests
  Principle 4 → IV. Isolate and Protect Persistent Data
  Principle 5 → V. Operable and Recoverable Services
Added sections: Additional Constraints; Development Workflow
Removed sections: none
Follow-up TODOs: none
-->

# BotFarm Constitution

## Core Principles

### I. Preserve Layered Ownership

Keep host, reusable core services, shared UI, and bot responsibilities distinct. Register
bot-specific services through the established named/keyed service patterns so one bot's runtime
state and dependencies cannot leak into another's. New project references or cross-layer
responsibilities MUST have a documented reason.

### II. Secure Configuration and Access

Load settings through the established .NET configuration pipeline and keep credentials out of
tracked files, logs, documentation, and test fixtures. Development-only authentication shortcuts
MUST NOT be enabled in production; production endpoints MUST retain their configured access
controls. This protects bot tokens, API keys, and operator accounts across environments.

### III. Behavior-Focused Automated Tests

Changes MUST add or update focused tests for their observable behavior in the existing test
projects. Tests MUST use substitutes for Telegram, MongoDB, and other deployed services rather
than requiring live external resources. Add integration coverage when a shared contract or
service boundary changes; keep routine tests deterministic and independently runnable.

### IV. Isolate and Protect Persistent Data

Keep each bot's persisted data isolated in its own MongoDB database; the shared Identity database
is an explicit exception. Data changes and cache invalidation MUST preserve that ownership
boundary. Backup and restore behavior MUST remain recoverable: restore operations pause affected
webhooks and process collections in a controlled manner.

### V. Operable and Recoverable Services

Services MUST provide useful health signals and actionable operational logs without exposing
secrets. Background work and shutdown MUST respect the host lifecycle. Changes to backup, health,
or deployment behavior MUST preserve a documented recovery path and be validated against the
operator workflow.

## Additional Constraints

- Preserve the established .NET solution structure and service-registration conventions. Add a
  runtime dependency only when it solves a demonstrated need and its operational cost is understood.
- Production containers run as non-root. Persistent backup archives require durable mounted storage.
- Preserve the scheduled daily backup and seven-archive retention policy unless a reviewed change
  documents the recovery and storage impact.
- Do not test against live Telegram, MongoDB, or webhook endpoints as part of the routine automated
  test suite; validate deployed integrations through their separate operational procedures.

## Development Workflow

- Before implementation, identify the owning project and relevant behavior tests. Keep changes at
  the narrowest layer that owns the behavior.
- Before review, run the narrowest relevant tests and build checks. Reviewers MUST check that the
  change respects these principles and that failures or skipped validation are reported.
- Changes affecting persisted data, external contracts, authentication, or deployment MUST include
  compatibility, migration, or rollback considerations appropriate to the risk.

## Governance

This constitution governs new and changed work. Amendments MUST be proposed as a reviewed change
to this file with rationale and impact. Changes to data, protocol, or deployment requirements MUST
include a compatibility or migration plan. During normal review, authors and reviewers MUST verify
compliance with the principles relevant to the change; any exception requires explicit rationale,
scope, mitigation, and approval in the change review.

Version this constitution using Semantic Versioning: MAJOR for incompatible principle changes or
removals, MINOR for new principles or materially expanded requirements, and PATCH for clarifying
or non-semantic edits. Update the last-amended date for every amendment; the ratification date is
the original adoption date and remains unchanged.

**Version**: 1.0.0 | **Ratified**: 2026-10-05 | **Last Amended**: 2026-10-05
