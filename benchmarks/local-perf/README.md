# Local performance lab

The local lab is the primary feedback loop for performance work. It compares a
frozen `main` worktree with the current candidate on the same Windows machine
and again in an ext4-backed WSL worktree. GitHub CI remains the final ARM/macOS
and clean-environment check, rather than the place where performance changes are
discovered one at a time.

The defaults target the current numeric-loop tranche:

- `int-arrays`
- `brainfuck`
- `count-primes`

The `int-arrays` script reports both `int32-kernel` and `accumulate` cases.

Node 22.23.2 is the competitive reference. Bun is deliberately absent from the
acceptance comparison: a locally installed Bun can still be run directly with
the cross-runtime harness, but its version is advisory and need not match the
repository pin.

## One-time worktrees

Keep all four source trees separate. Benchmark Windows sources on NTFS and Linux
sources on WSL's ext4 filesystem; `/mnt/d` is useful as a Git transport, not as a
Linux benchmark location.

This checkout uses the following conventional paths:

| Role | Path |
|---|---|
| Windows baseline | sibling `.perf-baseline-worktree` |
| Windows candidate | the current worktree |
| WSL bare transport | `~/src/SharpTS-perf.git` |
| WSL baseline | `~/src/SharpTS-perf-baseline` |
| WSL candidate | `~/src/SharpTS-perf-candidate` |

Create the Windows baseline from the exact `origin/main` commit that starts a
performance tranche. Keep it detached until the whole tranche is ready for a
PR. The WSL bare repository should have a `windows` remote pointing at the
Windows repository through `/mnt/d`; create detached baseline and candidate
worktrees from it.

The sync operation refuses a dirty Windows candidate or tracked changes in the
WSL candidate. This makes each cross-OS checkpoint reproducible:

```powershell
git add -A
git commit -m "perf: checkpoint numeric loop work"
./scripts/perf-local.ps1 -Action sync
```

The WSL toolchain is initialized explicitly by the script: .NET comes from
`~/.dotnet`, NVM selects Node 22.23.2, and Bun is not part of acceptance.
On Windows, install the pinned Node version side by side with
`nvm install 22.23.2`. The lab invokes that binary directly and does not change
the global NVM selection, so unrelated worktrees can continue using Node 24.

## Fast loop

Use the Windows checkpoint gate while editing, then measure one or two focused
workloads. Commit a coherent checkpoint before adding WSL:

```powershell
# Fast correctness checks on the active platform
./scripts/perf-local.ps1 -Action gate -Platforms windows -GateLevel checkpoint

# Alternating baseline/candidate launches on Windows
./scripts/perf-local.ps1 -Action measure `
  -Platforms windows `
  -Workloads int-arrays,brainfuck `
  -Runs 3

# After committing: sync and repeat on both operating systems
./scripts/perf-local.ps1 -Action sync
./scripts/perf-local.ps1 -Action measure `
  -Platforms windows,wsl `
  -Workloads int-arrays,brainfuck,count-primes `
  -Runs 5 `
  -Enforce
```

Each launch alternates which variant runs first. The report uses medians for the
displayed timings and the median of per-launch paired changes for decisions, so
thermal or host-load drift cannot turn mismatched independent medians into a
false result. It records raw data, commits, dirty state, host facts, JSON, and a
Markdown summary under the ignored `.perf-local/` directory. A regression is
material only when it exceeds both defaults: 10% and 0.05 ms. Override either
threshold explicitly when a workload needs a different noise floor.

## Gates and PR cadence

`checkpoint` builds Release, smoke-compiles the selected cross-runtime and
microbenchmark workloads, and runs the compiler-focused core tests. `full` runs
the entire hermetic core suite and adds GUI and conformance checks, packaging
helpers, the analyzer ratchet, and a compact WSL Native AOT publish/execute
check.

Run the full gate and a five-launch paired measurement when a performance
tranche is ready:

```powershell
./scripts/perf-local.ps1 -Action all `
  -Platforms windows,wsl `
  -GateLevel full `
  -Runs 5 `
  -Enforce
```

Batch several related, independently tested optimizations into one PR. Preserve
the intermediate commits and include the final `.perf-local/.../summary.md`
table in the PR description. GitHub then validates the architectures not covered
locally instead of serving as the development loop.
