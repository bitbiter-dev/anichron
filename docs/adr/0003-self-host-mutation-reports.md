# Self-host mutation reports; do not use the Stryker Dashboard

Status: accepted

## Decision

The mutation report is published to our own GitHub Pages site, and the badge is built from the JSON report. The Stryker Dashboard is not used.

## Why

- Stryker.NET's dashboard reporter uploads the full source of every mutated file; the report schema requires it, and the reporter has no score-only mode.
- It also needs OAuth access to the repository, and the hosted report's visibility and retention are undocumented.

## Consequences

- CI builds the badge itself (`scripts/mutation-report.sh`, `scripts/publish-badges.sh`).
- The Pages site serves `coverage/` and `mutation/` side by side.
- There is no history-over-time view. The JSON reports are CI artifacts and can be aggregated later.
