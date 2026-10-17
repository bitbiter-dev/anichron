# Design process

How an issue gets from a conversation to code. Why it works this way is in
[ADR 0006](../adr/0006-design-before-implementation.md).

## Roles

| Role | Who | Does |
|---|---|---|
| **Maintainer** | the human | Requirements engineer and decision maker. States what is needed and why, in conversation. Decides `needs-decision` issues. |
| **Analyst** | an agent | Turns the conversation into issues (`/to-spec`, `/to-tickets`, `/triage`). Writes the requirement, the first caller and the design part. |
| **Architect** | an agent | Writes design notes. Approves design notes written by another session. Raises decisions to the maintainer. |
| **Implementer** | an agent | Builds `ready-for-agent` issues (`/implement`). |
| **Reviewer** | an agent and CI | mattpocock's `/code-review`, and the architecture tests in CI. |

One agent session may hold several roles, but a design note is approved by a session other than
its author.

## Requirements come from conversation

The maintainer states requirements by talking, not by filling in a form. The analyst writes down
what was said. Anything the analyst **inferred** rather than heard (a scope boundary, an edge
case, a priority) goes back to the maintainer as a question before the issue is published. This
applies to `/to-spec` too: its "do not interview" covers what was already discussed, not
inferences.

## States

```
needs-triage → ready-for-agent → PR                                   no design trigger
needs-triage → needs-design → ready-for-agent → PR                    architect approves the note
needs-triage → needs-design → needs-decision → ready-for-agent → PR   the maintainer decides
any state, or an open PR → objection → needs-design | needs-decision
```

`needs-design` and `needs-decision` are this repo's additions to the triage states
(`triage-labels.md`). An issue in either state is design work: a `needs-design` issue is picked
to write or approve its note, and a `needs-decision` issue waits for the maintainer.
Implementation starts from `ready-for-agent`.

## What every issue carries

Whatever shape the skill produces, a `ready-for-agent` issue carries three parts:

- **Requirement**: the behaviour wanted, from the user's or caller's point of view, with
  acceptance criteria. It says *what*, leaving *how* to the design. This is the skill's own
  behaviour section: *What to build* (`/to-tickets`), *Problem Statement* and *Solution*
  (`/to-spec`), *Desired behavior* (the `/triage` agent brief).
- **First caller**: the code, endpoint or user that first uses what the issue builds. That caller
  ships in the same PR, or the issue is linked as blocking or blocked by the issue that delivers
  it.
- **Design**: a link to the approved design note, or `Design: not needed because <reason>`.

In an issue body, the last two are `## First caller` and `## Design` sections. In a `/triage`
agent brief they are fields in the brief's own style, `**First caller:**` and `**Design:**`.

**Publishing:** label a new issue `ready-for-agent` only when no design trigger applies or its
design note is approved; otherwise `needs-design`. This overrides a skill's own instruction to
apply `ready-for-agent`, including a `**Status:**` field in its template.

The *Issue readiness* workflow (`scripts/issue-readiness.sh`) checks every `ready-for-agent`
issue after #261. An issue missing its first caller or design part goes back to `needs-triage`,
with a comment naming what is missing.

**Legacy issues:** issues up to #261 predate this process and lack the last two parts. For those,
the implementer runs the trigger check below itself, and raises an objection when a design note
was needed but is missing.

## When a design note is needed

An issue needs a design note when it does any of these:

- introduces a new abstraction, interface, or project-level type;
- changes the schema or a public API contract;
- extends an existing pattern to a kind of thing it was not made for;
- spans the API and the Worker;
- makes a choice that is **hard to reverse, surprising without context, or a real trade-off**
  (the `/domain-modeling` ADR test, read as "any one of the three" rather than all three).

Otherwise the issue says `Design: not needed because <reason>`. When unsure, it needs one.

## Writing a design note

Post it as a comment on the issue, headed `## Design note`:

```markdown
## Design note

**Problem and constraints:** what must be true, and what limits the solution.

**Options:** at least two, each with how it works and what it costs. When the issue extends an
existing pattern, one option is changing that pattern.

**Recommendation:** which option and why.

**First caller:** who uses this first, and how that shapes the interface.

**Hard to reverse:** what this commits us to (schema, contract, data), or "nothing".

**Decision needed:** (only if the maintainer must choose) the question, the options, and the recommendation.
```

The note is done when every option names its cost, the recommendation follows from the
constraints, and the first caller is concrete enough to test against.

## Approving

- **Architect approval:** a session other than the note's author checks that the options are
  real, that changing the existing pattern was weighed, and that the first caller exercises the
  interface. It comments `Design approved` with one line of reasoning, puts a link to the note in
  the issue's design part, and moves the issue to `ready-for-agent`.
- **Maintainer decision:** when the note has a *Decision needed* section or is marked hard to
  reverse, the architect moves the issue to `needs-decision` instead. The maintainer decides in
  conversation, usually through `/grill-with-docs`. The agent records the decision on the issue,
  writes an ADR when the `/domain-modeling` test holds, and moves the issue to `ready-for-agent`.

## Objection

Every agent, in every role and every skill, raises an **objection** when it believes the
requirement, the design or the spec is wrong for the system, including when the maintainer wrote
it. Match the surrounding code for style; when the pattern itself does not fit, object. The
objection replaces any workaround.

1. Post the objection on the issue (or the PR):

   ```markdown
   ## Objection

   **Asked to:** what the issue or design says to do.
   **Why it does not fit:** the concrete conflict with the system, with evidence (code, ADR, measurement).
   **Options:** what could be done instead, each with its cost.
   **Recommendation:** which option and why.
   ```

2. Move the issue to `needs-design` for a design problem, or `needs-decision` for a requirement
   or a trade-off only the maintainer can settle.
3. Stop working on that issue and report the objection to whoever invoked the session.

The objection is done when the issue carries it and the new label, and no code for the issue is
committed.

## Adding an architecture rule

A decision that can be checked by looking at types and their dependencies becomes an
architecture rule in `src/Anichron.Architecture.Tests.Unit` (ArchUnitNET), not prose.

1. Add the rule to `ArchitectureRules.All` with a kebab-case name. The tests pick it up.
2. Run the project. Every current violator the rule finds is either fixed in the same PR or
   gets an entry in `architecture-exceptions.json`: the rule, the type's full name, the **open**
   issue that removes the violation, and the reason.
3. The PR that fixes that issue deletes its entries. Three checks keep the list honest: a rule
   fails on any violator without an entry, an entry fails once its type no longer violates, and
   `scripts/arch-exceptions-check.sh` in CI fails once the entry's issue is closed.

A rule is done when it fails on a deliberate violation (show the output in the PR) and passes
with only issue-linked exceptions.

## Skill by skill

| Skill | In this repo |
|---|---|
| `/grill-with-docs`, `/grilling` | The maintainer conversation: requirements, and `needs-decision` issues. Decisions land on the issue; ADRs per the `/domain-modeling` test. |
| `/to-spec` | *Implementation Decisions* holds only decisions the maintainer made or an approved design note contains; anything else becomes a design note. Add `## First caller` and `## Design`. Ask about inferences first (see above). Label `needs-design` when a trigger applies, otherwise `ready-for-agent`. |
| `/to-tickets` | Same parts and labelling per ticket, including its `**Status:**` field. A ticket that only builds a building block is blocked by, or bundled with, its first caller. |
| `/triage` | Before posting the agent brief, check the triggers; a trigger moves the issue to `needs-design` instead of `ready-for-agent`. The brief carries `**First caller:**` and `**Design:**`. Transitions into and out of `needs-design` and `needs-decision` are normal, not unusual. |
| `/implement` | First check the label is `ready-for-agent` and the issue carries its three parts (for legacy issues, run the trigger check instead); otherwise stop and say so. While building, raise an objection the moment the design stops fitting. |
| mattpocock's `/code-review` | Pass the issue **and its design note** as the spec, and list this file among the standards sources. The spec axis then reports departures from the note and any pattern extended past its purpose. |
| `/improve-codebase-architecture` | A tool for the scheduled architecture review (#260), alongside the review procedure. |

## Keeping this in step with the skills

The skills belong to the mattpocock plugin and are never edited here; this file and CLAUDE.md's
*Engineering process* section are where the repo adapts them. `/setup-matt-pocock-skills`
regenerates `docs/agents/issue-tracker.md`, `triage-labels.md` and `domain.md`, so keep those
files as the setup writes them, except for the *Repo-specific states* table in
`triage-labels.md`, which a re-run must keep. After a plugin update, re-read the skills named in
the table above and check each row still matches what the skill says.
