# Self-host mutation reports; do not use the Stryker Dashboard

Status: accepted — not yet implemented

We will publish the mutation report to our own GitHub Pages site and generate the score badge
ourselves from the JSON report, rather than using the hosted Stryker Dashboard.

**Implementation state.** This ADR records the decision only. The report publishing (#181) and
badge generation (#182) are not yet built; today the reports exist only as local output. The
Consequences section below describes intended work, not current behaviour.

## Why, when the dashboard is the obvious path

The dashboard is free for open source, hosts the report, and hands you a badge — which is
precisely what we want and would have saved writing a badge step. We decline it because of what
it uploads.

The mutation-testing report schema marks each file's `source` field **required**, defined as the
full source of the original file. Stryker.NET's dashboard reporter publishes that same report
object, so enabling it uploads the complete source of every mutated file. Stryker.NET exposes no
score-only mode: the score-only payload documented for the dashboard is reachable only by calling
its HTTP API by hand, not through the reporter. Enabling it also requires granting OAuth access
over the repository.

The hosted report's visibility, access control and retention are not documented anywhere we could
find. We were unwilling to make "our source is now on a third-party host, indefinitely, with
unknown visibility" an accidental consequence of wanting a badge.

## Consequences

CI has to construct the badge itself, reading the score out of the JSON report and writing a
shields.io endpoint payload onto the existing badge branch beside the coverage badge. This is a
small committed script rather than inline workflow steps, so it can be run against a saved report
and verified — a badge step that silently produces the wrong number would be worse than no badge.

Publishing both reports to one Pages site also required restructuring it: the coverage report
moves out of the site root into its own path, the mutation report gets a sibling path, and a
minimal landing page at the root links both. This changes the existing coverage report URL, and
the README badge target moves with it.

We give up the dashboard's history-over-time view. If we later want trend data, the JSON reports
are already CI artifacts and can be aggregated without uploading source.
