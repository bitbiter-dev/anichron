# One PR in CI at a time; full runs weekly

Status: accepted (decided on #536 and #537)

## Decision

- **Landing:** agents land their own PRs through `scripts/land.sh`, under a lock shared by every worktree on the machine. It merges master, marks the PR ready before pushing, so no draft run follows the ready one, waits for `CI gate` and `CodeQL`, and squash-merges without the maintainer's confirmation.
- **Drafts:** a draft PR runs no CI or CodeQL job. Its gate job passes under the name `Draft, no CI`, so a commit has no `CI gate` until its ready run reports one.
- **Master:** a push whose tree passed `CI gate` on its PR only publishes images.
- **Full runs:** the full backend and UI sweeps, the quality reports and the badges run weekly. A failed weekly run or master push opens an issue assigned to the maintainer.
- **Local:** mutation runs locally before landing, under a machine-wide lock. A clean merge of master needs no new run; a resolved conflict in the mutation scope does. The pre-push hook runs only `git lfs pre-push`.

## Why

- **Up-to-date branches make parallel CI waste a run:** two PRs in CI at once means one of them reruns after merging master. Serializing removes that run.
- **A skipped required check counts as passed:** a draft whose gate were skipped would satisfy branch protection on the commit it is later marked ready on.
- **Branch protection counts every check of a required name on the commit:** a failed `CI gate` from a draft's run would block the merge after the ready run passed.
- **The squash merge of an up-to-date head is the tree its CI tested,** so a second full run on master only repeats it.
- **Local mutation before landing** keeps each survivor from costing a full CI run.

## Rejected

- **Turning off the up-to-date requirement:** PRs could merge in parallel, but master would catch conflicts between them only after the merge.
- **Mutation only in CI:** saves local time, but every survivor costs another full run.
- **Cancelling a draft's run instead of skipping it:** a cancelled run still bills the minutes it ran.
- **A merge queue:** not offered for a repository owned by a user.

## Consequences

- PRs land one after another; each waits for the one in CI before it.
- A change that weakens the tests of a file nobody touched is caught by the weekly run, after the merge, unless its PR mutated the whole scope.
- Badges and the Pages reports change weekly, not on every merge.
- A draft's run still bills its gate job.
- Marking a PR ready before the push starts CI and CodeQL on the old head, which the push's runs cancel after they have billed a few minutes.
