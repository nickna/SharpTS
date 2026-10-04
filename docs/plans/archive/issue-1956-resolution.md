# Issue #1956: escaped numeric closure verification

Verified on Windows ARM64, .NET SDK 10.0.401/runtime 10.0.12, Release, on
2026-10-04. The existing `EscapedNumericClosureKeepsLiveUpdates` source and its
`13 14\ntrue Infinity\n` expectation remain enabled and unchanged.

An independently restored/built archive of reported baseline
`78931b4add5a6937ab6e63452f2e0ad97fe12adb` reproduces the test-host access violation
in `CastHelpers.IsInstanceOfClass`, called from `$Program.create(Double)`.
Compiling that same source with the baseline CLI and `--standalone --verify`
reports:

```text
[IL Error] $Program.create: StackObjRef {Offset=98, Found=Double} - Expected an ObjRef on the stack.
```

The emitted method loads the captured `Double current` field at IL offset
`0x005C`, then applies `isinst $LexicalUninitialized` at `0x0062` without boxing.
The verifier therefore identifies an invalid object-reference operation,
independently of the execution exception.

An independently restored/built archive of current main
`987a8b58d5fe32ddac4d8edd25384c923665e301` passes both original API modes, and its
standalone assembly passes IL verification. Its generated method omits that
sentinel check on the promoted numeric slot. The responsible existing repair is
`1331f8f06` (PR #1969), in `ILEmitter.EmitLexicalAssignmentTdzGuard`: value-type
capture fields cannot hold the object-valued TDZ sentinel, so the guard returns
before loading and inspecting them.

This consolidation adds deployment coverage for the original source and live
update/TDZ controls, including both declaration paths, verified standalone
execution and actual hosted initialization. It credits the existing product
repair; it does not claim a new compiler repair or infer behavior on other
architectures from the local exception.
