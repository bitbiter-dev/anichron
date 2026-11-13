# Design process

How an issue gets from a conversation to code. Why: [ADR 0006](../../docs/adr/0006-design-before-implementation.md).

## Roles

| Role | Who | Does |
|---|---|---|
| **Maintainer** | the human | States requirements in conversation; decides `needs-decision` issues |
| **Analyst** | an agent | Turns the conversation into issues (`/to-spec`, `/to-tickets`, `/triage`) |
| **Architect** | an agent | Writes and approves design notes; raises decisions to the maintainer |
| **Implementer** | an agent | Builds `ready-for-agent` issues |
| **Reviewer** | an agent and CI | `/code-review`, and the architecture tests |

One session may hold several roles, but another session approves a design note.

**Requirements come from conversation.** Anything the analyst inferred rather than heard (scope, edge case, priority) goes back to the maintainer as a question before the issue is published.

## States

```
needs-triage → ready-for-agent → PR                                   no design trigger
needs-triage → needs-design → ready-for-agent → PR                    architect approves the note
needs-triage → needs-design → needs-decision → ready-for-agent → PR   the maintainer decides
any state, or an open PR → objection → needs-design | needs-decision
```

A `needs-design` issue is picked only to write or approve its note. A `needs-decision` issue waits for the maintainer.

## What every issue carries

A `ready-for-agent` issue has three parts:

- **Requirement:** what is wanted, with acceptance criteria. It says *what*, not *how*. This is the skill's own behaviour section.
- **First caller:** the code, endpoint or user that first uses what is built. It ships in the same PR, or a blocking link connects the two issues.
- **Design:** a link to the approved design note (`#issuecomment-…`) or ADR, or `Design: not needed because <reason>`.

In an issue body the last two are `## First caller` and `## Design`; in a `/triage` brief they are `**First caller:**` and `**Design:**`.

- Label a new issue `ready-for-agent` only when no design trigger applies or its note is approved; otherwise `needs-design`. This overrides any skill's own labelling.
- `scripts/issue-readiness.sh` moves a `ready-for-agent` issue missing a part back to `needs-triage`.
- Issues up to #261 predate this. Their implementer runs the trigger check and objects when a note was needed.

## When a design note is needed

When the issue does any of these:

- introduces a new abstraction, interface or project-level type;
- changes the schema or a public API contract;
- extends a pattern to a kind of thing it was not made for;
- spans the API and the Worker;
- makes a choice that is hard to reverse, surprising without context, **or** a real trade-off.

When unsure, it needs one.

## Writing a design note

A comment on the issue:

```markdown
## Design note

**Problem and constraints:**
**Options:** at least two, each with its cost. When extending a pattern, one option changes the pattern.
**Recommendation:**
**First caller:** who uses it first, and how that shapes the interface.
**Hard to reverse:** what this commits us to, or "nothing".
**Decision needed:** (only if the maintainer must choose) the question, options, recommendation.
```

## Approving

- **Architect:** another session checks that the options are real, that changing the pattern was weighed, and that the first caller exercises the interface. It comments `Design approved` with one line of reasoning, links the note in the issue's design part, and moves the issue to `ready-for-agent`.
- **Maintainer:** a note with *Decision needed*, or one marked hard to reverse, goes to `needs-decision`. The maintainer decides in conversation, and the agent:
  1. records the decision in a `## Decision` comment;
  2. writes an ADR when the `/domain-modeling` test holds;
  3. moves the issue to `ready-for-agent`, or back to `needs-triage` with its new release label;
  4. updates the milestone order.

## Objection

Every agent objects when it believes the requirement, design or spec is wrong for the system, including the maintainer's. The objection replaces any workaround.

1. Post it on the issue or PR:

   ```markdown
   ## Objection

   **Asked to:**
   **Why it does not fit:** the concrete conflict, with evidence.
   **Options:** each with its cost.
   **Recommendation:**
   ```

2. Move the issue to `needs-design` (a design problem) or `needs-decision` (a requirement or trade-off).
3. Stop, commit no code for it, and report to whoever invoked the session.

## Adding an architecture rule

A decision checkable on types and dependencies becomes a rule in `src/Anichron.Architecture.Tests.Unit`, not prose.

1. Add it to `ArchitectureRules.All` with a kebab-case name.
2. Fix every current violator, or add it to `architecture-exceptions.json` with the rule, the type, the **open** issue that removes it, and the reason.
3. The PR that fixes that issue deletes its entries. A stale entry fails the tests.

Show the rule failing on a deliberate violation in the PR.

## Architecture review

`scripts/arch-review-due.sh` opens an "Architecture review due" issue after 10 merged PRs (Dependabot's excluded) since the last `arch-review/YYYY-MM-DD` tag. That issue goes before anything else.

1. **Scope:** the range from the last tag to master. Look for design smells, patterns extended past their purpose, building blocks without a caller, and `if`/`else` used as structure.
2. **Self-check:** re-verify every finding against current master.
3. **File:** one issue per high-impact finding, and one grouped issue for the rest, each with the three parts.
4. **Order:** link them as blockers of the work they affect, and put them next in the milestone orders.
5. **Close:** tag the reviewed commit `arch-review/<date>`, push the tag, and close the review issue.

## Skill by skill

| Skill | In this repo |
|---|---|
| `/grill-with-docs`, `/grilling` | The maintainer conversation. Decisions land on the issue. |
| `/to-spec` | *Implementation Decisions* holds only what the maintainer decided or an approved note says. Add the two sections, and ask about inferences first. |
| `/to-tickets` | The same parts and labelling per ticket. A building-block ticket is blocked by, or bundled with, its first caller. |
| `/triage` | Check the triggers before posting the brief. |
| `/implement` | Check the label and the three parts first, and object as soon as the design stops fitting. Then land without pausing (CLAUDE.md, "Landing work"). |
| `/tdd` | The seams come from the issue, its design note and CLAUDE.md's test seams; ask the maintainer only when they leave one open. |
| `/code-review` | Means `/mattpocock-skills:code-review`. Pass the issue **and its design note** as the spec, `origin/master` as the fixed point, and this file as a standard. |
| `/improve-codebase-architecture` | A tool for the architecture review. |

The skills are never edited here. This file and CLAUDE.md adapt them. After a plugin update, check each row above still matches its skill.
`/setup-matt-pocock-skills` regenerates `docs/agents/`; a re-run must keep the *Repo-specific states* table in `triage-labels.md`.
