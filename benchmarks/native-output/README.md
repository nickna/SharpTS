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
