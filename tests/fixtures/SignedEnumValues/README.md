# Signed enum values (#1788)

`negative_fraction.ts` and `negative_caught.ts` preserve the displayed original
sources and expected Node outputs. Fresh unchanged main and pre-repair current
verify but fail reverse lookup for -2. The caught source prints `negative-miss`,
`Half`, then fails forward access to Values.Negative. These fresh executions exit
with errors within 30 seconds; the historical deadline is not reproduced and no
process-shutdown cause is inferred.

Eight TypeScript/Node references cover negative/positive/zero/fractional values,
duplicates with last-member reverse mapping, forward access, signed-literal
auto-increment, parentheses/nested signs, namespace enums and const-enum controls.
TypeScript 7.0.2 and Node 25.5.0 establish expectations before SharpTS execution.

Compilation and checking share signed numeric literal recognition. Unary sign
and grouping nodes retain numeric values and allow subsequent implicit members;
ordinary enum tables retain their forward values and numeric reverse keys.
Arbitrary computed enum initialization remains a separate #1790 concern. Existing
emitted reverse-lookup metadata, helper signature and scoped ownership remain
unchanged; its missing-key behavior is tracked separately by #1789.

Saved checks require verified IL, exact stdout, empty stderr, no SharpTS
reference/copy and clean exit within the unchanged 30-second limit. Hosted checks
verify declarations without invoking exports. See the epic outcome record for
the selected native, enum and type-checker regression checks.
