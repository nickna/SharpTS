# Issue #189 — `--verify` false positives: ILVerifier mixes ref-assembly and runtime-assembly resolution

Branch: `fix/189-ilverifier-runtime-resolution` (worktree `.claude/worktrees/issue-189-ilverify-resolution`)

## Problem

`--compile … --verify` reports ~2,386 false IL errors (`StackUnexpected`,
`ThrowOrCatchOnlyExceptionType`) on any compiled program. The IL is fine — the
same DLL verifies clean (815 method bodies, 0 errors) when the ILVerify
resolver points solely at the shared-framework runtime directory.

## Root cause (confirmed in code)

Two facts collide:

1. **What the output references.** `ILCompiler.SaveToBytes`
   (`Compilation/ILCompiler.cs:1238`) only rewrites assembly references from
   `System.Private.CoreLib` to the ref-assembly facade surface when
   `_useReferenceAssemblies` is true **or** the output references SharpTS.
   The plain CLI path (`--compile` without `--reference-assemblies`) emits via
   `PersistedAssemblyBuilder`, so the DLL references **System.Private.CoreLib**
   directly.

2. **Where the verifier resolves from.** `Program.cs:700` defaults the
   verifier SDK path to `SdkResolver.FindReferenceAssembliesPath()` (contract
   assemblies). `ILVerifier.ResolveAssembly` (`Compilation/ILVerifier.cs:116`)
   probes that path **first** and falls back to the runtime directory only for
   files missing there. `System.Private.CoreLib.dll` does not exist in
   ref-assembly directories, so it falls back to the runtime dir while the
   facades (`System.Runtime`, …) resolve from ref assemblies. Mixed universe →
   core type identities never unify → ILVerify flags nearly every stack
   interaction.

The unit-test path (`TestHarness.CompileAndVerifyOnly` / `CompileVerifyAndRun`)
masks this because it compiles with `useReferenceAssemblies: true`, so the
rewriter makes the DLL reference facades and the ref-assembly universe is
self-consistent.

## Fix

Make `ILVerifier` resolve from the **runtime directory first** (the shared
framework dir contains both `System.Private.CoreLib` and the type-forwarding
facades, so both rewritten and non-rewritten output verify in one unified
universe — this is exactly the spike's 0-error reference implementation,
`SharpTS-spike-csmerge/spike/MergeTool/Verifier.cs`).

### Steps

1. **`Compilation/ILVerifier.cs`**
   - Make the constructor's SDK path optional: `ILVerifier(string? sdkPath = null)`.
   - In `ResolveAssembly`, swap probe order:
     1. runtime directory (`Path.GetDirectoryName(typeof(object).Assembly.Location)`)
     2. `_sdkPath` (only if provided — kept as an explicit `--sdk-path`
        escape hatch / extra probe dir)
   - Extract the duplicated "open + cache PEReader" block into a small local
     helper while in there.

2. **`Program.cs` (`VerifyCompiledAssembly`, ~line 697)**
   - Stop defaulting to `SdkResolver.FindReferenceAssembliesPath()`; pass the
     user's `--sdk-path` through as-is (may be null). Delete the
     "Cannot verify IL - SDK reference assemblies not found" early-return —
     the runtime dir always exists.

3. **`SharpTS.Tests/Infrastructure/TestHarness.cs` (`VerifyIL`, ~line 1103)**
   - Drop the `FindReferenceAssembliesPath()` lookup + throw; construct
     `ILVerifier()` with no SDK path.

### Non-goals / leave alone

- `AssemblyReferenceLoader` and `ILCompiler`'s use of ref assemblies for
  **compilation** metadata — correct as-is; ref assemblies are the right
  surface for compile-time reference resolution, just not for verifying
  CoreLib-bound output.
- Resolving user `--reference` DLLs / SharpTS.dll during verification (the
  existing "Failed to load assembly" filter in TestHarness covers this);
  separate issue if ever needed.

## Verification

1. **Repro before/after** (Release build, per repo conventions):
   `dotnet run -c Release -- --compile tmp/map.ts -o tmp/out.dll --verify`
   with a trivial `arr.map(x => x * 2)` script — expect
   "IL verification passed." (was 2,386 errors).
2. **Rewritten path still clean:** existing IL-verification unit tests
   (`CompileAndVerifyOnly` / `CompileVerifyAndRun` consumers) must still pass —
   facades in the runtime dir type-forward to CoreLib, and ILVerify follows
   forwarders, but this is the one assumption worth proving with the suite.
3. **`--sdk-path` still honored:** spot-check that passing an explicit
   `--sdk-path` doesn't crash (probed second now).
4. Full `dotnet test` run, output piped to a file and read (per
   feedback_test_runs memory).

## Outcome notes (implementation)

- Implemented as planned; repro went from 2,386 errors to "IL verification
  passed.", and the DLL still runs (`[2, 4, 6]`). Explicit `--sdk-path`
  re-checked and still passes.
- Bonus: the `KnownRuntimeErrors` allowlist in
  `SharpTS.Tests/CompilerTests/ILVerificationTests.cs` (URL/CookieJar/Headers/
  Web Streams "known IL errors") was entirely an artifact of the same mixed
  resolution universe — removed, and all IL-verification tests now assert
  zero errors unfiltered.
- Added regression test `CoreLibBoundOutput_PassesILVerification` compiling
  with `useReferenceAssemblies: false` (the CLI shape) and asserting zero
  verification errors.

## Risk

Low. The change narrows resolution to one self-consistent universe; the only
behavioral risk is the test-harness (facade-referencing) DLLs, covered by the
existing suite. No change to emitted IL.
