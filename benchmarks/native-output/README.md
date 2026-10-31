# Generated output measurements

Compare one immutable compiler's managed DLL, bundled managed executable,
and true NativeAOT output on a matching native toolchain:

```powershell
./benchmarks/native-output/run.ps1 -Compiler /path/to/SharpTS.dll -RuntimeIdentifier win-x64
```

The script retains publication logs, source/compiler/output hashes, deployment and
executable sizes, fresh-process wall times, observed peak working sets, and nine
in-process steady-state samples. Each variant must produce the same checksum.
Bundled and native executables run with an empty PATH and no DOTNET_ROOT variables.
The normal CLI bundler selects the managed executable's runtime requirements;
an empty PATH still permits an apphost to find an installed .NET runtime.
Native publication uses the compiler's normal strict warning policy and saved-IL
verification. It requires the matching .NET SDK and native compiler/linker.

Startup samples follow one unrecorded warmup and include process creation, loading,
and minimal console output. They measure warm fresh-process latency, not time to
the first instruction. Steady-state samples follow five in-process warmups and
measure the same numeric kernel with Date.now; zero milliseconds means below that
clock's resolution. Memory is the operating system's observed process peak working
set, and remains null if unavailable. Deployment size excludes logs, optional
PDB/DBG/dSYM symbols and the native cache manifest; symbol and total output-directory
sizes are retained separately. Publication time includes cache lookup and toolchain
work; retain the logs to distinguish cache hits from fresh publication.

Run on an otherwise idle machine and retain the raw JSON when publishing results.
An explicit OutputDirectory must be new or empty, so stale artifacts cannot affect sizes.
Concurrent CI or compiler builds make the numbers diagnostic only. The default
15 launches are intended for local comparisons, and this single kernel does not
establish general application performance. macOS bundled output requires its
supported SDK bundler prerequisites.

## Windows x64 measurement, 2026-10-30

The [raw samples](results/win-x64-2026-10-30.json) use the compiler source at
`c1f56e665` plus the integral Date clock fix in `6c36b772c`, SDK 10.0.403 and
`DOTNET_PROCESSOR_COUNT=2`. All owned compiler/test jobs were paused for this run.
Each variant has 15 startup launches and nine steady-state samples with checksum 21.

| Output | Publish seconds | Deployment MiB | Startup median ms | Steady median ms | Steady process peak MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Managed DLL | 1.22 | 0.33 | 101.8 | 13 | 29.8 |
| Bundled managed EXE | 1.20 | 0.49 | 104.4 | 13 | 30.6 |
| NativeAOT | 18.10 | 6.43 | 55.9 | 13 | 13.8 |

Managed deployment sizes exclude the installed shared .NET runtime. Native symbols
are excluded from deployment size and recorded separately in the raw data. The native
image passed the strict publish and image-header checks. The one-millisecond clock
and single numeric kernel limit the interpretation of these timings; they establish
this measured workload rather than general application performance.
