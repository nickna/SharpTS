# Number predicate method values (#1801)

The exact `original.ts` and `call-control.ts` both pass on unchanged main
`0ad37b57` and the epic branch: standalone IL verifies, outputs match the issue's
Node expectations, exit is zero and stderr is empty within the original
30-second execution deadline. This task verifies a prior repair; it makes no
production change. Commit `1ed687cb3704acc68869e91da89d2b1774ebf24e` refreshes
retained interface value bindings after declaration merging, including Number's
ES5 constructor value when ES2015 members are later added.

The issue's historical rejection is retained:
`Property 'isNaN' does not exist on type 'NumberConstructor'. Did you mean 'NaN'?`
The direct value expression `Number.isNaN` in the identity comparison is the
relevant lookup: the any-valued alias calls and metadata accesses do not require
that declared member. A focused direct-member check with `--lib es5` still
rejects it with the NumberConstructor missing-member diagnostic. Modern default
libraries accept it. The old historical compiler is not presented as freshly
executed, and the ES5 control is not represented as the historical setup.

`method-values.ts` checks each predicate as a function value, inferred aliases,
cached identity, names/arities and non-coercing results. CLI negative tests
reject nonexistent direct/aliased members and incompatible function/result
types. Existing declaration-merge and same-named-interface shadowing checks
remain green. `shadowed-interface-control.ts` checks the NumberConstructor
shadowing case through the loaded-library interpreted CLI.

Reference commands (each file independently):

```powershell
node --experimental-transform-types --no-warnings tests/fixtures/NumberPredicateValues/original.ts
tsc --noEmit --skipLibCheck --target es2022 tests/fixtures/NumberPredicateValues/original.ts
```

The original, call control and method-value source are saved standalone outputs:
`--no-tsconfig --compile <source> -o <output> --standalone --verify`, with a
60-second compile bound and the original 30-second execution bound. They omit
SharpTS.dll/references and match Node with clean exits. Node v25.5.0 and
TypeScript 7.0.2 accept the four positive reference sources. Namespace syntax
requires Node transformation rather than strip-only execution.

Independent new-control failures are retained without weakening their Node
expectations or counting them as passing saved conformance:

- `typed-alias-independent-control.ts` uses `typeof Number`; unchanged main and
  the current loaded-library checker incorrectly resolve the instance interface
  instead of NumberConstructor. The no-library harness separately lacks that
  typeof global binding.
- `namespace-initializer-independent-control.ts` expects `true isNaN 1` but
  unchanged-main/current compiled execution prints nothing.
- The exported-function `shadowed-interface-control.ts` expects `true` and
  passes the interpreted CLI, but unchanged-main/current saved execution throws
  `TypeError: object is not a function`. This remains a namespace boundary,
  separate from the repaired direct Number predicate lookup.

All 95 selected scoped tests pass with compiled IL verification. Required
quality gates pass. Production is unchanged from #1799's actual zero-warning
AOT analyzer baseline check. Ignored `artifacts/epic-1866/1801-*` logs retain
observations and failed controls. No typeof-query, namespace execution or hosted
guest execution repair is claimed.
