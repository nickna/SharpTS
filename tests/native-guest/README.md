# Native generated-program corpus

This corpus exercises genuine NativeAOT **generated programs** using the managed
SharpTS compiler and the supported SDK/ILC publish flow. It is separate from
NativeAOT compilation of the SharpTS compiler host.

The CI cohort runs on matching Windows, Linux and macOS x64/ARM64 runners:

- `language`: loops, numeric functions, callbacks, arrays and JSON.
- `arguments`: spaced arguments and a nonzero exit code.
- `modules`: an imported function and module initialization.
- `collections`: Map, Set and regular expressions.
- `generators`: generator creation and iteration.
- `async`: Promise construction and continuation callbacks.
- `parameter-defaults`: eager generator defaults and async rejection keep later
  parameters in TDZ even when an initialized outer binding has the same name.
- `exceptions`: guest object identity/properties across a catch.
- `bcl`: direct immutable CLR constructor, member and property access, plus
  `RuntimeFeature.IsDynamicCodeSupported == false` in the executed guest.

Run that cohort after building the managed compiler:

```powershell
dotnet build src/SharpTS/SharpTS.csproj -c Release
./scripts/test-native-guest.ps1 -RuntimeIdentifier win-x64
```

Every selected program must pass saved-IL verification and strict native publish,
produce a matching native image, and execute with `PATH` empty and all
`DOTNET_ROOT` overrides removed. Output, exit status, compiler/source/binary hashes
and original tool logs are retained in a unique results directory. Windows images
must have no CLR header; Unix images must have the matching ELF or Mach-O header.
Any coded trim/AOT diagnostic or ILC invalid-method report fails the run before
execution. No warning suppression or root-everything descriptor is used.
Windows also changes a real linker option to force a missing-input failure,
checks that the cache misses and preserves prior output, and repeats the
unchanged successful publication.

The collection and Promise fixtures joined the CI cohort after their constructor
reflection findings reached zero and both executed as native Windows programs.
CI verifies the same strict contract on all six RIDs. The CLI preserves original
SDK diagnostics and promotes no failed output. This cohort does not complete
[#1351](https://github.com/nickna/SharpTS/issues/1351)'s class, third-party closure,
networking, crypto, P/Invoke, benchmark or native debugger acceptance
work.

Each matching runner also packs the candidate `SharpTS.Sdk`, verifies its managed
build/rebuild/clean/publish behavior, and publishes a native consumer through
`dotnet publish`. That consumer must preserve its guest portable PDB, pass saved
IL verification, produce a matching native executable, and preserve spaced
arguments and exit code 7 with no `dotnet` on PATH.

The recorded elapsed times are test timings, including process/log overhead.
They do not replace the release performance benchmark. Native symbol files and
guest portable PDBs do not establish TypeScript stepping in a native debugger.
