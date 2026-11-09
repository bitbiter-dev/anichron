# Design before implementation; agents object instead of complying

Status: accepted

## Decision

Agents write the issues, designs and code; the maintainer sets requirements in conversation and makes the decisions. Design is a step before implementation, carried by design notes, the first-caller rule and objections. The procedure is `.claude/docs/design-process.md`.

## Why

The 2026-10-16 architecture review found four serious problems in clean, fully tested code. All of them entered before any code was written:

- Issues prescribed implementations, and agents built exactly what was written.
- Building blocks shipped without a caller, so nothing exercised their shape.
- Review compares code with the spec, so a wrong spec passes.
- Agents imitate existing patterns and had no sanctioned way to say "this does not fit".

Every check measured code; nothing measured design.

## Rejected

- **More review after the fact:** it catches the problem once it is built, against the same wrong spec. A scheduled architecture review stays as a backstop.
- **The maintainer approves every design:** a bottleneck. An architect agent approves, and the maintainer sees only what is hard to reverse or needs a decision.
- **A separate issue template:** the skills' own shapes are extended instead.

## Consequences

- An issue with a design trigger takes one extra step before `ready-for-agent`.
- An objection is a good outcome: a round trip is cheaper than a refactor.
- Decisions that can be tested become architecture tests rather than prose.
