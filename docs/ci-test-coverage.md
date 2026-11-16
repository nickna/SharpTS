# PR and daily test coverage

Ordinary code PRs retain all hermetic core regressions, in interpreted and
compiled modes, on Windows and Linux. Four core jobs per OS partition whole
classes using measured class-duration hints. Classes missing from the timing
file are still discovered and assigned; the timing file is not an allowlist.
Collections retain their existing xUnit serialization within each runner.

Build/package checks, core tests, GUI tests and benchmark smoke checks run as
independent jobs. Process integration, editor/DAP, TypeScript smoke, AOT warning
ratchets and native-host compilation remain PR checks.

## Exhaustive deployment rows

`DeploymentData` explicitly marks standalone data sweeps. Each annotated method
runs its first two rows for every combination of boolean/enum arguments on a
routine PR. For example, hosted and non-hosted cases both remain represented.
These are deployment permutations; the ordinary semantic regression suite is
not sampled. Facts, InlineData, SerializableMemberData and ordinary MemberData
still run in full. New tests are PR tests unless explicitly opted into this
attribute. Order the rows so the first examples exercise representative positive
and negative boundaries; unique deployment regressions should remain Facts or
ordinary data tests rather than being opted into a sweep.

The standalone runner discovers both Full and Pr inventories from the same
assembly. It rejects a missing PR method or a PR row absent from the full suite.
Its cadence artifact lists PR and deferred rows, preserving duplicate discovery
entries. Daily runs use Full, including every deferred row. No environment
variable means Full, so ordinary local and release commands remain exhaustive.

GUI editing/lifecycle contracts stay on PRs. The four SharpPaint light/dark
presentation permutations have `Cadence=Daily`; GUI-affecting changes run them
before merge as well. Full and PR-plus-deferred GUI discovery must match exactly.
Benchmark smoke keeps harness/schema checks and the exceptions workload on
ordinary PRs; daily runs compile every workload on both operating systems.

## Schedules and overrides

* CI: daily at 10:23 UTC. Manual `workflow_dispatch` also selects Full.
* Native generated programs: daily at 10:43 UTC across all six existing RIDs.
  Representative PR coverage retains every native guest case on Windows x64,
  Linux x64 and macOS ARM64.
* Desktop GUI: daily at 10:53 UTC on both existing default desktop platforms;
  explicit ARM64 real-window certification remains manually selectable.

Add the `ci:full` PR label or dispatch CI manually to request exhaustive coverage.
Deployment/build/harness changes automatically use full standalone coverage;
changed standalone methods and directly referenced semantic data-provider classes
retain all their rows even on the PR route;
GUI and benchmark changes expand their respective suites. See
`scripts/get-test-coverage-scope.ps1` for the reviewed path rules. The first PR
introducing these workflow changes intentionally takes the full route.
Schedules only become active once the workflows land on the default branch;
GitHub may delay scheduled starts. Scheduled failures produce failed Actions
runs and must be triaged rather than rebaselined automatically.

## Evidence and validation

The Gate requires all four core and all eight standalone shard artifacts for
each OS, matching inventory digests, exact-once assignment, nonempty TRX results
matching executed classes/methods and at least the discovered number of results
(opaque theories can expand at execution). A missing, duplicate, mismatched or
failed shard cannot make the workflow green. The combined core shards must also
match the independently discovered core/process partition, so mutually
consistent shards cannot silently omit part of core.

Run the policy regressions with:

```powershell
./scripts/test-ci-test-plans.ps1
./scripts/test-core-test-partitions.ps1
./scripts/test-standalone-shard-plan.ps1
./scripts/test-github-workflows.ps1
```

After a Release build, inspect or run the same plans locally:

```powershell
./scripts/test-core-shard.ps1 -ShardIndex 0 -DescribeOnly
./scripts/test-core-shard.ps1 -ShardIndex 0
./scripts/test-standalone-shard.ps1 -ShardIndex 0 -ShardCount 8 -Coverage Pr -ResultsDirectory artifacts/standalone-pr
./scripts/test-standalone-shard.ps1 -ShardIndex 0 -ShardCount 8 -Coverage Full -ResultsDirectory artifacts/standalone-full
./scripts/test-gui-conformance.ps1 -Coverage Pr
```

Run all indices for complete local coverage. Preserve TRX artifacts and compare
hosted-runner wall times after rollout. The initial 15–20 minute PR objective is
a target, not a timeout guarantee; full deployment overrides take longer. Core
keeps its existing 30-minute test timeout until hosted timings justify tightening
it. Update
`.github/core-test-durations.json` from measured TRX results as the suite evolves.
Do not delete slow semantic regressions to make the target appear satisfied.
