# Companion observations outside the frozen #1912–#1918 targets

These observations surfaced in controls while reconciling the named historical failures.
They are recorded separately, without starting implementation work or expanding the
historical repair claims. Product/test baseline:
`e622d0c88f9133d10ce6809082a3085e759fb42b`, Windows, October 1, 2026
(America/Los_Angeles), SDK 10.0.401/runtime 10.0.12, Node 25.5.0.

| Companion | Recoverable source and fresh result | Scope boundary |
| --- | --- | --- |
| Interpreted unary-plus exotic hook | [#1915 source](issue-1915/explicit-exotic.ts): Node prints `9 9 numbernumber\n`; interpreter prints `9 NaN number\n`; compiled output is `NaN 9 number\n`. Full paths are retained in its results.json. | #1933 owns the original compiled explicit Number conversion, not this independently observed interpreted unary-plus failure. |
| Interpreted radix-36 control | [#1916 source](issue-1916/radix.ts): parse('zz',36) gives Node/compiled `1295`, interpreter `NaN`. Full outputs and CLI paths are retained. | #1934 owns the original compiled explicit hexadecimal-prefix failure, not this base-36 control. |
| String-valued raw input in interpreted String.raw | Original #1677 TemplateMetadataPrograms row 5 contains `String.raw({raw:'ABC'},'x','y')`; the fresh default interpreted CLI prints `undefinedxundefinedyundefined` instead of `AxByC`. Other row outputs are `a1b2c` and `p9q`. The committed original phase's parity expectation is `a1b2c\nAxByC\np9q\n`. | #1914's historical targets are CLI declaration acceptance and the computed-tag receiver. Current declaration acceptance does not establish this neighboring runtime case's correctness. |
| Default-library Math.sumPrecise / DataView typing | [#1917](issue-1917/results.json) preserves missing Math.sumPrecise interface diagnostics; [#1918](issue-1918/results.json) preserves DataView's rejected interface ArrayBuffer argument. `--noLib` paths reach the named original runtime observations. | These declaration boundaries are not relabeled as the original runtime defects, and do not enlarge the runtime-fix issue acceptance contracts. |

The first two programs were already bundled with the original recovered sources, but
their newly observed interpreted control failures were not named in the frozen phase
reports. No claims about root cause, existing duplicate ownership or full compatibility
are made for these separately recorded findings.
