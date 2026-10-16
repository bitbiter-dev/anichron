# Design before implementation; agents object instead of complying

Status: accepted — partially implemented (architecture tests #257, rule migration #258, readiness check #259 and review cadence #260 are open)

Agents write the issues, the designs and the code; the maintainer sets requirements in
conversation and makes the decisions. Design is a step before implementation, carried by design
notes, the first-caller rule and objections. The working procedure, the single source for how,
is `docs/agents/design-process.md`.

## What went wrong, which is the only reason to take this decision

The architecture review of 2026-10-16 (the 12 commits after #234) found four problems serious
enough to file as issues (#251–#254). None of them was careless code: the code was clean, tested
to a 100% mutation score and followed every convention. The errors entered before any code was
written:

- **Issues prescribed implementations.** #242 said the BlurHash "is stored as a `ProxyFile` like
  any other proxy, so read its (tiny) file", which led to a disk read per asset. #149 specified a
  500-line `IFileSystem` decorator down to its base classes. Agents built exactly what was
  written.
- **Building blocks shipped with no caller.** `AssetDtoMapper`, `BlurhashReader` and
  `FindVisibleAsync` were built ahead of the endpoint that would use them, so nothing exercised
  their shape.
- **Review compares code against the spec.** When the spec is wrong, review passes.
- **Agents imitate.** Extending the existing pattern was always the path of least resistance, and
  no agent had a sanctioned way to say "this does not fit".

Every check the repo has (analyzers, format, mutation testing) measures code; nothing measured
design. The fix is therefore a step in the process, not another rule for the code.

## Considered options

- **More review after the fact**, e.g. a stricter code review. Rejected as the primary fix: it
  catches the problem once it is built, and it reviews against the same wrong spec. A scheduled
  architecture review stays, as a backstop (#260).
- **The maintainer approves every design.** Rejected: it makes the maintainer the bottleneck. An
  architect agent approves; the maintainer sees only what is hard to reverse or explicitly needs a
  decision.
- **A separate issue template.** Rejected in favour of extending the shapes the mattpocock skills
  already produce (`/to-tickets`, `/to-spec`, `/triage`'s agent brief), so there is one shape
  rather than two competing ones.

## Consequences

- Issues that hit a design trigger take one extra step before `ready-for-agent`.
- An objection is a good outcome. It costs a round trip; building something believed to be wrong
  costs a refactor later, and #251–#254 are that price.
- Decisions that can be tested become architecture tests (#257) rather than prose (#258).
