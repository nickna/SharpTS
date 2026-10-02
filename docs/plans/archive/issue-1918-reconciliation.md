# BigInt dispatch, literal generators, assignment and aliases (#1918)

All four original observations still fail on the current baseline and transfer
independently to [#1939](https://github.com/nickna/SharpTS/issues/1939),
[#1940](https://github.com/nickna/SharpTS/issues/1940),
[#1941](https://github.com/nickna/SharpTS/issues/1941) and
[#1942](https://github.com/nickna/SharpTS/issues/1942). The completed BigInt metadata
ownership migration stays credited; this reconciliation implements no runtime fix.

## Recovered sources and individual dispositions

Frozen #1599 L99 and [PR #1683](https://github.com/nickna/SharpTS/pull/1683) explicitly
preserve async static dispatch, literal generators, typed-array assignment and CommonJS
alias limitations. All four programs and two positive controls in
[cases.json](issue-1918/cases.json) are recovered verbatim from the original final
head `3e4fb4b256615f53080e6bc88ad9eae8c9238f06`'s `BigIntMetadataPrograms`.
Original parity outputs remain separate from the Node/reference expectations.
The isolated phase used 30-second execution deadlines. Original saved binary/log bundles
were not recovered from the linked comments or retained primary-checkout artifacts;
the exact committed programs and expectations are recoverable. No old compile deadline
is inferred from the current collector's separate per-operation budget.

| Source / observation | Node/reference output | Interpreted API | Compiled API, standalone and actual hosted initialization | Disposition |
| --- | --- | --- | --- | --- |
| `async-static.ts`: post-await BigInt.asIntN | `-1n 9n\n` | Matches | `ReferenceError Undefined variable 'BigInt'.\n` | Exact failure; #1939. Caught rejection is incorrect output despite zero subprocess exit status. |
| `generator-literal.ts`: yield bigint literals | `14n\n18n\n` | Matches | `literal generator failed\n` | Exact failure; #1940. The catch marker is preserved original evidence, not an accepted result. |
| `typedarray.ts`: dynamic value[0]=1n | `-1n 123n 2 18446744073709551615n 123n\n` | Same first line plus `assignment failed\n` | Same failure as interpretation | Exact assignment failure in both runtimes; #1941. The Node source has no post-write read, so this establishes absence of its failure marker, not a new comprehensive storage-conformance claim. |
| `constructor-alias.cjs`: B('123') | `123n 123n 255n ff\n` | Matches | `123n null 255n ff\n` | Exact alias invocation failure; #1942. Direct conversion, static alias and radix output are positive controls. |
| `async-call-control.ts`: callable BigInt after await | `7n\n` | Matches | Matches | Recovered passing control; it does not test static dispatch. |
| `generator-call-control.ts`: yield BigInt('7') / BigInt('9') | `7n\n9n\n` | Matches | Matches | Recovered passing control; it does not test literal representation. |

## Paths, duplicate check and evidence

Product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`, Windows,
SDK 10.0.401/runtime 10.0.12, Node 25.5.0, October 1, 2026 (America/Los_Angeles).
[results.json](issue-1918/results.json) retains actual evidence-branch HEAD, source hashes,
all output/diagnostics and deployment metadata. Earlier branch commits change evidence
and collectors, not product behavior. [historical.json](issue-1918/historical.json)
preserves L99 and its hash.

Default and ESNext CLI typing reject the typed-array source before execution:
`DataView buffer must be an ArrayBuffer or SharedArrayBuffer, got 'interface ArrayBuffer'.`
That diagnostic is preserved as a separate declaration boundary, not mistaken for the
indexed-write failure. `--noLib`, the original isolated phase's typing mode, reaches
the original runtime failure in interpreted CLI and standalone/hosted output. The other
five sources compile under the default path as well; default standalone execution
reproduces the relevant compiled outputs. Twelve builtin-typed standalone/hosted
assemblies and five default assemblies pass IL verification. None references or
co-locates `SharpTS.dll`; hosted output adds the hosting ABI reference. No passing verifier
or caught-error exit status is counted as runtime correctness.

Open/closed searches and local BigInt/emitter history found no exact fixes before
#1939–#1942. Closed #912 concerned typeof, primitive conversion, formatting and mixed
equality, not these four sources. #1919 owns the distinct primitive constructor-alias
reconciliation; its Number/Boolean/resolver scope is not silently expanded into this
BigInt issue. The four independently testable behaviors each have their own acceptance
contract, original reference output and correction destination.

```powershell
dotnet tests/fixtures/SharpTS.HistoricalEvidence/bin/Release/net10.0/SharpTS.HistoricalEvidence.dll cases docs/plans/archive/issue-1918 artifacts/issue-reconciliation/issue-1918
```

The collector records failures without accepting them as test expectations. All 244
existing owner cases and repository code-quality gates pass. Existing parity assertions,
workloads, numeric limits and deadlines remain unchanged; no broader BigInt compatibility
or ownership-audit closure is claimed.
