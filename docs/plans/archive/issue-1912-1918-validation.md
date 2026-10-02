# Consolidated verification for #1912–#1918

Each of the seven reconciliation reports gives its original observations, evidence
limits, current status and independently scoped correction destinations. Thirteen
remaining defects transfer to #1930–#1942. The implementation of those fixes is separate
from these bounded evidence tasks. No completed ownership migration is reopened.

Verification on Windows, October 1, 2026 (America/Los_Angeles), SDK 10.0.401/runtime
10.0.12; product/test baseline `e622d0c88f9133d10ce6809082a3085e759fb42b`:

- Release test-project and final diagnostic-project builds pass, with existing NU1902
  package warnings. The diagnostic executable is outside the default solution.
- 72 timing/allocation/lifetime/timer/watcher cases and 244 existing owner cases pass,
  with zero skips/failures. These are focused selections, not a fresh full-suite claim.
- The original isolated console restoration test deliberately reproduces its retained
  first-invocation failure. The exact second invocation and both public-writer controls
  pass. The collector preserves the failure instead of accepting it as correctness.
- The recovered original one-sample bitwise workload produces 64 after 1,000,000
  iterations, allocating 96 bytes against 4,096; the 1,000-iteration warmup produces 232.
- Nineteen programs execute across Node, interpreted/compiled APIs, CLI typing paths,
  standalone output and actual hosted initialization. Thirty-eight builtin-typed and
  sixteen default CLI compilations pass IL verification. Wrong outputs are preserved
  independently of verifier success. The three rejected default declaration programs
  are identified in their reports, not counted as executed default-path cases.
- TypeScript 7.0.2 accepts seventeen of the nineteen original/matched programs with
  ES2024 target / ESNext,DOM libraries. It rejects two Math.sumPrecise property accesses;
  Node 25.5.0 also lacks that runtime method. All reference diagnostics are retained in
  per-issue `reference-compilation.json`; the sum expectation is specification-derived.
- Repository code-quality self-tests and analyzer/CLI mutation gates pass: unchanged
  28 approved duplicate groups, zero errors. All nine workflow guards pass.
- The frozen ledger verifier covers all 459 original nonempty lines and 18 comments.
  The new retained-evidence verifier checks all nineteen source hashes, exact manifest
  expectations/provenance, frozen excerpt bindings, deployment/IL records, console
  controls and the original numeric workload. `git diff --check` passes.

Recheck the retained evidence without execution:

```powershell
pwsh scripts/verify-historical-evidence.ps1
pwsh scripts/verify-1599-reconciliation.ps1 -IssueJson <saved-original-1599-response.json>
```

The original frozen response is retained in the primary checkout at
`D:/nickna/SharpTS/artifacts/issue-1926/issue-1599.json`; it is not a portable prerequisite
for the new per-issue evidence verifier. Raw fresh logs/TRX/output remain in this
worktree's git-ignored `artifacts/issue-reconciliation`. Checked-in execution JSON
replaces only the absolute worktree prefix with `<repository>`; source bytes and source
hashes are unchanged. Later evidence/collector commits change no product or existing
test behavior, so their recorded Git HEADs are distinguished from the product baseline.
