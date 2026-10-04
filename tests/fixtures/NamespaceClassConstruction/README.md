# Namespace class construction (#1781)

`original.ts` preserves the frozen issue's displayed source and `3 5 true`
expectation. Fresh unchanged main and pre-repair current save it but fail IL
verification with exactly one object/System.Type StackUnexpected error at offset
165. The isolated qualified-only path fails at offset 79. Neither invalid saved
output is executed. The alias-only control already verifies and prints `5 true`,
with empty stderr and clean exit within the unchanged 30-second deadline.

The qualified path now casts the namespace lookup result to System.Type before
Activator.CreateInstance, as the generic path already did. Nine compiled and
isolated saved standalone references cover the original, qualified/aliased and
computed construction, nested namespaces, argument order, inheritance, member
functions and explicit generic arguments. Full TypeScript ES2022/CommonJS
compilation and Node confirm every expectation. Every saved output must verify
IL before execution, preserve exact stdout and empty stderr, exit within 30
seconds, and contain no SharpTS assembly reference or copy.

The seven existing namespace-class verifier failures retained by earlier epic
tasks now pass. Native namespace metadata/deployment coverage remains intact.
The same-named sibling/module declaration identity problem remains separately
recorded in `docs/plans/namespace-class-identity.md`; this construction cast does
not repair that independent registry/owner problem.
