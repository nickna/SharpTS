# Reflect.apply result typing (#1798)

`original.ts` and `direct-control.ts` retain the exact issue sources and their
Node expectation, `function 6`. Unchanged main `0ad37b57` rejects the original
before saving an assembly: `Cannot assert type 'R' to 'number'.` The direct
control already succeeds. No execution or IL verification is claimed for the
rejected baseline source.

The issue also retains an earlier TypeScript preparation diagnostic, TS2365:
adding one directly to an unknown Reflect.apply result was rejected. Its exact
preparation source was not included in the issue body available here. The
unchanged, TypeScript-valid asserted original remains the acceptance source;
no reconstructed preparation source is presented as historical evidence.

The repair keeps each ambient overload's own generic binder, infers function
rest argument tuples, preserves homomorphic readonly tuples/arrays, and keeps
call/construct signatures when instantiating a generic call. This selects the
typed result where applicable while allowing the original any-valued target's
assertion. The two invalid fixtures remain TypeScript/SharpTS errors. CLI tests
also reject readonly/optional/rest result mismatches, invalid targets, and
explicit arguments that violate the tuple constraint. Ambient overload tests
cover distinct binders, scope isolation, fallback after a failed constraint,
and readonly writes. Existing Reflect.construct invalid-target checks pass.

Reference checks (compile each positive file separately):

```powershell
node --experimental-strip-types --no-warnings tests/fixtures/TypedReflectApply/original.ts
tsc --noEmit --skipLibCheck --target es2022 tests/fixtures/TypedReflectApply/original.ts
```

Saved-output checks use `--no-tsconfig --compile <source> -o <output> --verify`
with normal deployment. Proxy outputs must receive the matching SharpTS.dll;
all six saved outputs compare deployed runtime bytes, verify IL, print their
Node expectations, exit zero and have empty stderr within the original
30-second execution deadline. The compile bound is separately 60 seconds.
No independent standalone Proxy deployment or hosted execution is claimed.

All 1,051 selected affected tests pass with `SHARPTS_VERIFY_COMPILED=1`.
Node v25.5.0 and TypeScript 7.0.2 accept all six positive fixtures. Required
quality gates and the actual AOT analyzer baseline pass with zero analyzer
warnings. Ignored `artifacts/epic-1866/1798-*` logs retain the baseline rejection,
the initial deployment-test setup failures, and the caught/corrected generic
construct-signature substitution regression.
