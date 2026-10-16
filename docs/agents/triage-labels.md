# Triage Labels

The skills speak in terms of five canonical triage roles. This file maps those roles to the actual label strings used in this repo's issue tracker.

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.

## Repo-specific states

This repo adds two states between triage and `ready-for-agent`. The skills do not know them; `docs/agents/design-process.md` says when to apply them, and moving into or out of them is a normal transition.

| Label            | Meaning                                                                      |
| ---------------- | ---------------------------------------------------------------------------- |
| `needs-design`   | Needs a design note (options, first caller, reversibility) before `ready-for-agent` |
| `needs-decision` | Waiting on a decision from the maintainer                                    |

They count as state labels like the five above: an issue carries exactly one state label, so moving to either replaces the previous one.
