# dotnet test regression investigation — September 27, 2026

Investigated commit `46640e915d129e87ac7996d543ec7da7ea171de0`, which also matches
the user's `D:\SharpTS` checkout. Windows, SDK 10.0.401, runtime 10.0.12, Debug.
The findings below describe the original checkout; the implementation follow-up
at the end records the subsequent fixes and validation.

## Findings

The supplied log contains two GUI failures and a separate, still-running core
suite. The GUI suite actually finished after 567 seconds. Its failures cannot
by themselves explain why `SharpTS.Tests` was still running at 3,707 seconds.

The strongest reproduced explanation for the long core run is a large serial
tail of standalone deployment tests. No infinite loop or deadlock was reproduced.
The original hour-long run has no per-test progress log or dump, so this
investigation cannot prove which test it was executing at that instant.

### 1. Default test runs include a rapidly expanded serial process workload

- Discovery lists 24,600 entries, including **1,223 in `StandaloneDllTests`**.
  Some theories expand further at execution, so discovery entries and executed
  test counts are not identical.
- These tests are in one xUnit class and therefore run sequentially. The
  collection parallelism in `tests/SharpTS.Tests/xunit.runner.json` does not
  parallelize the cases within that class.
- Many cases launch a fresh compiler with verification, then a fresh executable
  process. They repeatedly pay process startup, JIT, assembly verification and
  filesystem costs instead of using the warmed in-process harness.
- I observed **216 standalone cases passing continuously** in the unrestricted
  core run. Their coarse console durations totalled 252.6 seconds, averaging
  1.17 seconds. This already implies roughly 24 minutes for 1,223 similarly
  priced cases, before slower cases and other work. This is an extrapolation,
  not a measured full-suite completion time.
- The source grew from **1,629 lines on September 1 to 16,071 lines today**.
  The baseline commit is `363e014c003509b710583d9343d1f6bf76f3aaa0`.
- CI already treats this as a separate workload: `.github/workflows/ci.yml:186`
  excludes the class from the core job. The standalone job runs three method
  shards on separate runners using `scripts/test-standalone-shard.ps1`.
  Commit `8e3dd513` introduced sharding on September 17; `ad78d1c2` increased it
  to three shards on September 18 to stay within the CI time budget.

After establishing continuous progress through that tail, I stopped the broad
diagnostic run and executed **all remaining core tests**, without excluding
network or npm categories:

**23,432 passed, 3 skipped, 0 failed; 3.4378 minutes (206.3 seconds).**

The VSTest hang collector reported that all tests finished. The full standalone
collection was not run to completion in this investigation.

References: `tests/SharpTS.Tests/CompilerTests/StandaloneDllTests.cs:19`,
`:14343` (`ExecuteCompiledDllIsolated`), and `.github/workflows/ci.yml:208`.

### 2. SharpPaint's failure is a per-process workflow deadline

`SharpPaintHeadlessTests.cs:12` gives each workflow process 180 seconds.
`InteractionsPassInInterpretedAndCompiledModes` runs interpreted first and
compiled second. The supplied failure occurred in the interpreted process;
compiled mode was never reached in that test. The timeout diagnostic confirms
the host was killed and its output collected successfully.

The expanded workflow combines editing, recovery, both presentation themes,
compact layouts, dialogs and DPI changes. Commit `689ef4e2` added the
presentation coverage; `aff7d497` subsequently increased the workflow deadline
from 90 to 180 seconds on September 8. The last printed checkpoint identifies
the last completed checkpoint, not necessarily a permanently blocked operation.

Observed results:

| Run | Result |
| --- | --- |
| Filtered reproduction of both reported tests, concurrent with the core diagnostic run | Both passed; SharpPaint combined test took 175.8 seconds |
| Timestamped interpreted host, same staged assets and workflow | Passed in 80.730 seconds; 63.938 CPU seconds; empty stderr |
| Timestamped compiled host, same workflow | Passed in 23.722 seconds; 14.031 CPU seconds; empty stderr |

The combined-test time is **not** the interpreted process time and should not
be compared directly with its 180-second limit. The timestamped diagnostic
harness allowed 300 seconds, but neither process needed an extension beyond
the normal 180-second limit. The interpreted diagnostic overlapped only the
late, mostly serialized portion of the second core run; the compiled run
finished after the core suite had completed.

In the timestamped interpreted run, `light / compact-layers` completed at
50.330 seconds, followed by `compact-effect` at 52.609 seconds and the remaining
workflows. A live dump from the earlier, slower run showed the GUI owner thread
actively evaluating interpreter calls during rendering, rather than waiting
on a deadlock. One stack sample is not a CPU profile and does not establish
which interpreter routine dominates runtime.

**Assessment:** a load-sensitive deadline on an expanded, expensive interpreted
workflow is supported by the evidence. A deterministic hang at compact layers
was not reproduced. The exact cause of the original slowdown remains unproven.

References: `tests/gui-conformance/SharpTS.Gui.Conformance.Tests/SharpPaintHeadlessTests.cs:82`,
`:174`, and `samples/SharpPaint/presentation.tests.tsx:21`.

### 3. Hot reload fails during cleanup, with insufficient diagnostics

The supplied stack ends at
`HotReloadIntegrationTests.cs:92`: an unguarded recursive `Directory.Delete`
inside `finally`. Windows refused deletion of `Avalonia.Base.dll`.
This is not a hot-reload assertion failure. However, a `finally` exception can
replace an earlier exception, so the original log also cannot establish that
every preceding assertion succeeded.

The test waits for the host to exit, then immediately deletes the staged
runtime. It has no retry budget for transient DLL access failures and does not
preserve the original exception if deletion fails. `SharpPaintHeadlessTests`
already tolerates `IOException` and `UnauthorizedAccessException` in its
equivalent staging cleanup.

Hot reload passed once alongside SharpPaint (11.35 seconds) and **six further
consecutive repeats** (2.97–3.09 seconds each). A leftover hot-reload directory
from the time of the user's run still contained Avalonia DLLs with normal
`Archive` attributes, consistent with interrupted cleanup rather than a
read-only source file. The process or service holding the file at the original
failure was not captured; antivirus, delayed release, and other file users
must remain possibilities rather than asserted causes.

**Confirmed defect:** cleanup can fail the test and mask its primary outcome.
**Unconfirmed:** the underlying holder of the original DLL lock.

### 4. Real hang-protection gaps exist, but were not observed causing this run

These warrant separate hardening rather than being labelled the reproduced
root cause:

- `Infrastructure/ExternalAssemblyFixture.cs:149` synchronously drains stdout,
  then stderr, **before** calling the nominal five-minute `WaitForExit`.
  A stuck child or full stderr pipe can prevent that timeout from ever running.
- `Infrastructure/TestHarness.cs:1429` (`CompileVerifyAndRun`) drains both pipes
  sequentially and then waits without a timeout.
- `IntegrationTests/CliPrimitiveConversionTests.cs:32`, `CliVarTests.cs:33`,
  `CliCommonJsCompiledTests.cs:35`, and several bundler tests use similar
  unbounded output/exit waits.
- `Infrastructure/TestHarness.cs:355` (`RunCompiledInProcess`) performs parsing,
  checking, emission and assembly loading before the timed execution task.
  The advertised execution timeout does not bound a compiler/type-checker hang.
- `IntegrationTests/CliTestHelper.cs:50` starts concurrent output reads and
  bounds process exit, but subsequently waits for pipe completion without a
  bound. An inherited pipe held by a surviving descendant is a separate risk.

The NU1902 restore warning is separate from these test failures. Restore and
build succeeded in both the supplied log and the reproduction.

## Recommended changes

1. Make the supported local test workflow match CI: explicitly separate core,
   GUI and standalone coverage, with progress output and bounded diagnostics.
   Split standalone tests into independent classes or projects if normal xUnit
   collection parallelism should cover them. Preserve every deployment case;
   simply excluding the class must not silently become the full test command.
2. Give SharpPaint editing and presentation scenarios independently reported
   tests, preferably by mode/theme, with phase timings retained on failure.
   Profile the interpreter under representative suite load before selecting a
   performance fix. Increasing the timeout alone does not address the cost.
3. Centralize staged-host cleanup with bounded exit/output waits and bounded
   retry for transient Windows file locks. Preserve primary test exceptions
   and record retained paths if cleanup cannot finish.
4. Replace the unbounded subprocess helpers with one helper that drains both
   streams concurrently and budgets execution, termination and pipe cleanup.
   Add a compiler-stage timeout or process boundary where a hard bound is needed.

## Reproduction and evidence

The following diagnostic command completed successfully. It requires an existing
Debug build and deliberately leaves standalone coverage for its separate run:

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --no-build `
  --filter 'FullyQualifiedName!~SharpTS.Tests.CompilerTests.StandaloneDllTests.' `
  --logger 'console;verbosity=normal' `
  --blame-hang-timeout 2m --blame-hang-dump-type mini
```

The blame hang limit detects inactivity; it is not an aggregate two-minute
suite limit and will not stop a long sequence of successful cases.

Local evidence is retained under `artifacts/test-investigation/` (git-ignored):

- `core-regression-unsandboxed.log`: broad run through 216 standalone passes,
  intentionally interrupted after confirming continued progress.
- `core-without-standalone.trx` and `.log`: completed remaining core suite.
- `test-list.txt`: test discovery inventory.
- `gui-regression.trx` and `.log`: both reported GUI tests passing.
- `hot-reload-1.trx` through `hot-reload-6.trx`: repeated cleanup checks.
- `interpreted-timing.json`, `compiled-timing.json`, and `*-phases.log`:
  per-mode CPU/wall timings and timestamped progress.
- `run-sharpaint.ps1`: reproducible staging/timing harness; retains its output.
- `sharpaint-live.dmp` and `sharpaint-stacks.txt`: live rendering stack evidence.

An initial sandboxed invocation stalled during restore and was cancelled before
testing. All reported measurements come from normally permissioned executions.
No product or test source was changed during the investigation.

## Implementation follow-up

The supported complete local command is now `pwsh ./scripts/test-local.ps1`.
It builds Release, runs core and GUI separately, then launches three standalone
method shards concurrently. All categories remain included unless `-Hermetic`
is requested. Default `dotnet test` coverage is unchanged. Each stage has logs,
TRX results, a 120-second inactivity dump guard and a 30-minute aggregate budget.
Discovery itself has a 60-second deadline. `CONTRIBUTING.md` documents focused
suites, existing builds, Debug configuration and configurable budgets/shards.

`tests/Shared/TestProcess.cs` now owns concurrent stdout/stderr draining,
execution deadlines and bounded cleanup for the core CLI/harness helpers and
GUI subprocess helpers. Diagnostics retain exit state, partial output and a
timestamped output timeline. An exited parent's inherited pipes cannot block
the test indefinitely. Process-tree termination is best effort while the parent
is alive; this does not provide OS-level containment for detached descendants.
`TestDirectory.TryDeleteAsync` retries transient file locks for two seconds,
then reports a retained path without replacing the primary test exception.

SharpPaint editing, light presentation and dark presentation run as six separate
mode/scenario tests. They preserve their original 180-second per-process guard,
window/render assertions and phase diagnostics. The sample's default `all`
workflow is still available outside the partitioned tests.

The in-process compiled harness now includes parsing, checking, emission and
loading in its timeout, as well as guest execution. It runs on a dedicated
worker, following the interpreter harness, to keep synchronous compiler/guest
work off the pool needed by asynchronous continuations. If compilation finishes
after its deadline, guest execution is skipped and cleanup still runs. Managed
compilation cannot be forcibly aborted; the outer test-process guards remain
the hard boundary for a genuinely stuck compiler.

The first implementation validation exposed two empty-output Promise failures
under a full parallel core run; the isolated Promise test passed. This prompted
the dedicated-worker change above. That run also exposed a concrete polling
bug in `DeterministicHostDispatcher.RunUntil`: 10,000 empty `Thread.Yield` polls
could exhaust its turn budget before an external callback arrived. It now
counts executed callbacks and separately limits elapsed time to five seconds.
Regression tests cover delayed external completion, missing work and an endlessly
replenished queue.

Validation evidence is retained locally under `artifacts/test-investigation/`
and `artifacts/test-hardening-validation/`. The new process fixtures cover large
stderr output, unterminated stdout, nonzero exits, live child termination and
inherited pipes after parent exit. Additional tests cover Windows file locks,
late compilation, and execution cancellation. The PowerShell supervisor was
also exercised directly against success, nonzero-exit and hanging tree fixtures.

The final full core run passed **23,442 tests, with 3 skipped and 0 failed**, in
4.7883 minutes while standalone shards were also active. Both Promise cases and
the hosted initialization case that failed in the first implementation run
passed. The complete GUI run passed **134 tests** in 4.4005 minutes. Standalone
partition discovery assigned all 152 methods exactly once across three shards.
The three standalone shards passed **390, 486 and 517 cases** respectively:
**1,393 passed, 0 failed**, with the slowest shard completing in 12.6598 minutes.
Execution expands some theories beyond the 1,223 discovery entries. The final
validation therefore totals **24,969 passed, 3 skipped and 0 failed** across
the successful stage results. Timing is not a clean benchmark: the final core
rerun and some focused checks overlapped the standalone run.

The initial `All` invocation correctly returned a failure exit code for its
preliminary core failures; those logs remain intact. Final core evidence is
`artifacts/test-hardening-final-core/core/core.trx`; the completed GUI and three
standalone TRX files are under `artifacts/test-hardening-validation/`. Final
focused checks also passed all four timeout assertions and all four GUI
hot-reload/forced-timeout cases. PowerShell partition discovery and the nine
existing CI workflow guards passed, as did `git diff --check`.
