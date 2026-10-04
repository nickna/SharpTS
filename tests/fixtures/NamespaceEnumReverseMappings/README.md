# Namespace enum reverse mappings (#1780)

`original.ts` preserves the frozen issue's displayed source. Fresh unchanged main
and pre-repair current save verified IL, print `3 2 undefined`, and exit with
empty stderr within 30 seconds. Current prints the original TypeScript/Node
expectation, `3 2 Second`.

Nine references cover named/computed/string keys, numeric reverse entries,
string/heterogeneous enums, duplicate values, fractions/zero, declaration
identity, same-named enums in nested/separate namespaces and merged namespaces.
Reverse numeric keys use the runtime's JavaScript string conversion. Namespace
enum registries are scoped by their enclosing path, and a bare enum value within
the namespace loads the published object, preserving identity.

Compiled and isolated saved standalone outputs require verified IL, exact stdout,
empty stderr, clean exit within the unchanged deadline and no SharpTS reference
or copy. Nine additional hosted compilation checks append an exported function
to enter the module path, verify IL and deployment, and require the hosting
abstractions reference without a SharpTS reference/copy. Hosted exported
functions are not executed. Scoped native enum/namespace helpers retain fresh
declaration ownership for minimal/optional/repeated standalone and hosted output.
Full TypeScript ES2022/CommonJS compilation and Node confirm every source.

`independent/negative-initializer.ts` retains the separate #1788 initializer gap,
excluded from passing counts. Its Node expectation is `Negative Fraction Zero
Fraction`; main prints four `undefined` values, and current prints `undefined
Fraction undefined Fraction`. Both saved diagnostics verify IL and exit cleanly
within the original deadline. Positive fractional mappings are repaired here;
the declared negative and negative-zero members are not yet registered correctly.
