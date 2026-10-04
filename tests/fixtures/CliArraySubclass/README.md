# CLI Array subclass controls (#1909)

`retained-storage.ts` copies the program retained in
`EmittedArrayStorageRuntimeTests.PackedSparseSubclassAndRestStorageVerifyAndRunStandalone`
from #1817, which the historical #1599 note identifies as its exact API control.
Its expected output is:

```text
1,4,3,5
100001 false boxed 9
1,boxed false
3 false 7
1,2,3,6
true 1
```

The historical separate non-generic source was not recovered. `nongeneric.ts`
is a labelled new control for its recorded `Superclass must be a class` diagnostic;
its expected output is `3 false 7` followed by `true true true`.

On unchanged `0ad37b57`, the default-library CLI rejects
the typed source with `Cannot use type arguments with non-generic class 'Array'`
and the non-generic declaration with `Superclass must be a class`. The retained
typed source also fails at pre-repair `313373c5`; both baselines compile it with
`--noLib`, matching the API's prior successful path.
The repair preserves libraries and recognizes their actual Array value binding,
without treating a shadowed local `Array` as the built-in.

TypeScript 7.0.2 accepts each fixture separately with ES2022 and `--noEmitOnError`;
Node v25.5.0 prints the output above. The repaired CLI interpretation and
IL-verified standalone execution match both references. Keep the original
60-second compilation and 30-second execution limits. Hosted execution is not
claimed. Integration and isolated standalone regressions cover default libraries,
`--noLib`, brands/holes/arguments, shadowed bindings and excess type arguments.
