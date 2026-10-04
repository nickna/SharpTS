# Shared async mutable captures — #1734

`original.ts`, `bounded.ts` and `outer-counter-control.ts` retain the exact
issue sources and Node expectations. Fresh unchanged main (`0ad37b57`)
IL-verifies, but the original loop hits its unchanged 30-second deadline with
empty stdout/stderr. The bounded case exits cleanly and prints four copies of
`1 false`, confirming that captured counter writes do not survive calls.
The historical and fresh timeout observations are retained without inferring
a process-shutdown cause.

Standalone async capture slots now retain an existing enclosing display-class
reference where the lexical binding has that home. Each invocation accesses
the original field, sharing writes with sibling sync/async closures and the
enclosing code. Capture homes resolve by lexical AST identity and references
by type, preserving independent factories and same-named global bindings.
The ordinary boxed storage contract also handles numeric display-class fields.

Eleven fixtures run in both engines and as isolated standalone outputs. They
cover the original loop/bounded steps/control, fresh factories, sibling reads
and writes, async function expressions, captured parameters, multiple lexical
owners and genuinely pending awaits. Saved tests require verified IL, absence
of runtime references/copies, exact Node stdout, zero exit and empty stderr
within the original 30-second deadline. All references pass Node and TypeScript.

The broader selected run has 446 passes and one unchanged-main failure:
`AsyncArrowFunctionTests.AsyncArrow_NestedWithMutation(Compiled)` still reports
`StackUnexpected` at offset 90 for the readonly boxed outer-state-machine
address, matching the retained #1714 baseline evidence. This issue repairs
captures with an existing reference-type lexical home; it does not claim that
separate boxed-state-machine repair, capture homes absent from existing
display-class planning, per-iteration mutable async cells or hosted execution.
