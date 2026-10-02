# Primitive constructor aliases and the reserved import (#1919)

The original Boolean and Number CommonJS alias failures still reproduce and transfer to
[#1944](https://github.com/nickna/SharpTS/issues/1944) and
[#1945](https://github.com/nickna/SharpTS/issues/1945), respectively. This is evidence
reconciliation; it changes no interpreter, compiler or original compatibility assertion.

## Recovery and individual dispositions

[cases.json](issue-1919/cases.json) recovers both alias sources verbatim from the final
heads of [#1684](https://github.com/nickna/SharpTS/pull/1684) and
[#1685](https://github.com/nickna/SharpTS/pull/1685), including their original parity
outputs. Reference expectations remain separate from those incorrect parity outputs.
[historical.json](issue-1919/historical.json) preserves frozen #1599 L67/L101/L103 and
binds each excerpt to the unchanged historical ledger.

| Form | Node / interpreted API | Compiled API, default standalone, builtin-typed standalone and actual hosted initialization | Disposition |
| --- | --- | --- | --- |
| `const B=Boolean; B(0); B('x')` in `.cjs` | `false true false true true\n` | `false true null null true\n` | Exact retained failure; #1944. Direct calls and alias identity remain positive controls. |
| `const N=Number; N('2')` in `.cjs` | `2 2 true\n` | `2 null true\n` | Exact retained failure; #1945. Direct conversion and identity remain positive controls. |
| User `import {create} from 'primitive:async_hooks'` | Node rejects the reserved URL scheme | Every CLI typing/compilation path rejects the reserved user import during resolution | Supported rejection, not a reachable missing-feature emitter defect. Original complete source bytes were not supplied; the fresh probe preserves the reviewed import form. |

The primitive-import observation is the withdrawn review in
[#1666](https://github.com/nickna/SharpTS/pull/1666#discussion_r4014488701).
`ModuleResolver` reserves that namespace for embedded stdlib origins. Node's rejection
does not define the SharpTS contract; the resolver restriction and reviewed supported
facade imports establish it. The collector's single-file API entries reject import syntax
before module resolution, so those entries are not module-path evidence. The CLI module
paths do reach and reproduce the recorded resolver diagnostic. No guest assembly or hosted
runtime is produced for the rejected form. The original numerical deadline is unavailable.

## Evidence, duplicate checks and verification

Product and collection baseline `78931b4add5a6937ab6e63452f2e0ad97fe12adb`, Windows,
.NET SDK 10.0.401/runtime 10.0.12, TypeScript 7.0.2 and Node 25.5.0; collected October 2,
2026 (America/Los_Angeles). [results.json](issue-1919/results.json) retains source hashes,
outputs, diagnostics, runtime/deployment metadata and the 30-second collection budget.
Only the absolute repository prefix is replaced with `<repository>`; the raw report is
under `artifacts/issue-1919-1925/run-1919`. The original alias fixtures also used 30-second
execution deadlines; no historical compilation deadline is inferred. Original saved binary
bundles are absent from the searched retained checkout artifacts and linked PR evidence.

Open/closed GitHub searches for Boolean/Number aliases and local conversion/dispatch history
found no exact later repair. B16 / #1915 covers Number coercion hooks and async lookup,
including #1932; it does not own this constructor alias. B19 / #1918 already recovered
BigInt's distinct constructor alias and transferred it to #1942; that observation is an
exact scope duplicate and is not republished. #1911/#1928 covers Map.groupBy lookup rather
than primitive constructor invocation. Shared causes are possible, but each constructor
has an independently verifiable acceptance contract.

All six alias default/standalone/hosted assemblies pass IL verification. None references or
co-locates SharpTS.dll; hosted assemblies add the hosting ABI reference. Incorrect alias
stdout persists despite valid IL and normal exits. The Release collector build and 297
focused owner/accessor regression cases pass with no failures or skips.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1919 artifacts/issue-1919-1925/run-1919
pwsh scripts/verify-reconciled-evidence.ps1 -Issue 1919
```

The integrity verifier checks retained evidence and references; it does not turn failing
runtime outputs into passing conformance assertions or claim broader API compatibility.
