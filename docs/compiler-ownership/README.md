# Compiler ownership outcomes: epic #1865

This closes the seventeen frozen boundaries in
[#1865](https://github.com/nickna/SharpTS/issues/1865). The frozen inventory is
`83a41096108fe6de739fa7cfcb148c4bb1193121`; implementation starts from
`1331f8f06cbc6456ab584c60e479891474c795bc`. Each boundary has its own completion
commit, owner/key/lifetime/phase analysis and execution evidence. Six boundaries
needed demonstrated improvements; eleven retain supported existing contracts.
Retention is an explicit outcome allowed by the epic, not an inferred fix from
inventory counts.

| Boundary / issue | Disposition and audit | Task commit | Passing targeted selections |
| --- | --- | --- | --- |
| C01 / #1883 | [Retain class/constructor owners](C01.md) | `7bdc7994` | 99 + 255 |
| C02 / #1884 | [Retain public member/alias views](C02.md) | `d34eb8b6` | 198 |
| C03 / #1885 | [Retain typed property ABI owners](C03.md) | `bc7dea20` | 86 + 76 |
| C04 / #1886 | [Fix full declaring-type identity for lock fields](C04.md) | `7e77545a` | 54 |
| F01 / #1887 | [Check generic parameters by exact method owner](F01.md) | `6be1c0f4` | 110 |
| F02 / #1888 | [Retain function/rest/metadata ownership](F02.md) | `3360220b` | 212 + 104 + 81 |
| L01 / #1889 | [Preserve callback binding identity; share adapter owner](L01.md) | `4a4a6148`, follow-up `355505ca` | 124 + 55 + 282 + 19 |
| L02 / #1890 | [Retain nested display-class ownership](L02.md) | `3a7523e1` | 230 + 83 |
| L03 / #1891 | [Retain per-module entry-point captures](L03.md) | `d7e05174` | 167 + 78 |
| S01 / #1892 | [Retain async analysis and emitted handles](S01.md) | `24a78dd7` | 494 |
| S02 / #1893 | [Retain generator/async-generator builders](S02.md) | `ffaeca92` | 836 + 74 |
| M01 / #1894 | [Allocate path-owned module prefixes; retain module namespace](M01.md) | `8117060b` | 73 + 354 |
| M02 / #1895 | [Retain enum snapshots and runtime value authority](M02.md) | `4d6e5eb2` | 124 |
| I01 / #1896 | [Reject mapper configuration from another owner](I01.md) | `e7da1a36` | 18 + 205 |
| I02 / #1897 | [Protect signature/result arrays, collectible lifetimes and context identities](I02.md) | `a502a88f`, context follow-up | 26 + 166 + 33 |
| I03 / #1898 | [Retain fixed catalogs and checked strategy registration](I03.md) | `8363e3b3` | 384 |
| I04 / #1899 | [Retain fixed runtime targets and deployment contracts](I04.md) | `45a790d7` | 106 + 17 + 22 |

Counts above come from actual successful TRX files under
`artifacts/epic-1865/<boundary>/`. Selections overlap and are not a count of unique
tests. The directory is intentionally ignored build output; the committed
regression tests and commands below reproduce the consolidated validation.

The evidence directory also preserves original failures for lock namespace
ownership, callback substitution/duplicate adapters/static blocks, colliding
module basenames and namespaces, foreign mapper owners and poisoned/shared CLR
cache arrays/collectible assemblies. C04's module-namespace observation was
explicitly transferred to M01 and resolved there. F01's exploratory generic
function-value invocation and L02's unsupported nested async-arrow capture writes
are documented existing semantic limitations, not claimed ownership repairs.
Enum numeric semantics in #1788–#1791 remain outside M02's frozen scope.

## Consolidated verification

Run from the repository root with .NET 10 and PowerShell 7:

```powershell
pwsh -NoProfile -File scripts/test-code-quality.ps1
pwsh -NoProfile -File scripts/test-local.ps1 -Suite All -Hermetic -ResultsDirectory artifacts/epic-1865/final
git diff --check
```

The local suite builds the solution, runs all core and GUI tests, then partitions
the entire standalone class across three shards. `-Hermetic` uses the CI
exclusions for `LiveNetwork`, `LoadSensitive` and `npm`; it does not assert those
external tests passed. Standalone tests execute saved assemblies in isolated
processes and check host deployment, while relevant new tests additionally run
saved-IL verification and interpreter/compiler comparisons.

Verified checkpoints: the Release solution build succeeds, Release core records
**24,122 passed / 0 failed / 3 existing Windows skips**, and GUI records **134
passed / 0 failed / 0 skipped**. Quality gates report **28 duplicate groups / 0
errors**. The final-source AOT analyzer inventory matches the zero-warning
baseline, and the L01 follow-up has **19 passing** Debug checks.

The full Debug core run at `355505ca` additionally records **24,128 passed / 0
failed / 3 existing Windows skips**. The standalone discovery contains **1,995
cases in 249 methods**, all included by the three-shard local partition.

The Release All run started before the final L01 traversal follow-up. An
additional full Debug core run checks that final source revision. The consolidated
PR's validation results report its totals and all three standalone shard totals;
local logs/TRX remain under the evidence directory above. PR CI supplies separate
Linux/Windows, six-shard standalone, conformance, packaging and Native AOT checks;
those results must be assessed at the current PR head.
