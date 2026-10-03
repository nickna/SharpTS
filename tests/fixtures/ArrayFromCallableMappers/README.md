# Array.from callable mapper regression (#1713)

`original.ts` preserves the issue's source. Node v25.5.0 and TypeScript 7.0.2
accept it; stdout is `2,4`, `2,4`, `1,2`, each on its own line. Unchanged
`0ad37b57` and pre-repair `d41c975e` interpret it correctly, but their IL-verified
saved output throws a guest TypeError on the bound mapper after printing `2,4`.

The repair validates emitted callable carriers before consuming the source,
then uses the existing receiver-aware invocation path. Number's indirect value
form now uses the numeric runtime owner's explicit coercion helper, including
BigInt, Symbol rejection and the missing-argument zero default.

Dual-mode controls cover earlier bound arguments and receiver precedence,
bound array methods, extracted function call wrappers, omitted/undefined
mappers and null/object/primitive mapper rejection on empty inputs. Isolated
CLI tests compile with default libraries, `--verify --standalone`, assert no
SharpTS assembly reference, and execute the exact original plus controls.
The new isolated limits are 60 seconds to compile and 30 seconds to execute;
the original issue did not specify a deadline.

`class-mapper-control.ts` is a separate new control. Node prints `0` then `true`;
both interpreter baselines print `0` then `accepted`. The compiled regression
retains Node's result. Classes have [[Call]] but throw when actually called, so
empty-input validation must accept them. The interpreter call/construction
boundary is a separate adjacent defect, not a repaired #1713 case. Publishing
an adjacent issue was rejected by automatic approval review as outside the
frozen epic authorization; this source and observation preserve the finding.
See [IsCallable](https://tc39.es/ecma262/multipage/abstract-operations.html#sec-iscallable).

This does not claim a complete callback-family implementation, proxy mapper
coverage, or hosted execution.
