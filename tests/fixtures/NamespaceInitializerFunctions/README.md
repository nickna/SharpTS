# Private namespace initializer functions (#1775)

The three `original-*` sources are the unchanged ordinary, generator and async
initializer probes from the frozen issue. Fresh unchanged main saves and
verifies their outputs, then reports an undefined `read` with empty stdout.
Current resolves all three after #1774's script-scope resolution repair and
prints `5`, `4,5` and `7`. Every run keeps the original 30-second deadline;
fresh diagnostic processes exit within it, leaving historical lifetime
observations separately tracked in #1772.

Compiled and isolated saved standalone tests cover all originals, nested name
shadowing, merged declarations, private function visibility and stable identity
when a private function is exported twice as a value. Namespace population
publishes exported functions while private function declarations remain lexical.
Every saved test requires verified IL, exact stdout, empty stderr, a clean exit,
and no SharpTS assembly reference or copy. Full TypeScript compilation and Node
confirm every expected result.

`private-typeof-independent.ts` retains #1778's separate missing-member behavior:
Node prints `5 undefined`, while current before #1778 prints `5 object` because
a missing member returns CLR null. #1778 repairs it and includes the unchanged
source in its compiled and saved regression coverage. The seven #1775 references
above retain their original counts. No broader
private-variable publication, cross-declaration private scope, namespace
mutation, class identity or hosted guest execution repair is claimed here.
