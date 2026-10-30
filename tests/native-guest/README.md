# Native generated-program corpus

This corpus exercises genuine NativeAOT **generated programs** using the managed
SharpTS compiler and the supported SDK/ILC publish flow. It is separate from
NativeAOT compilation of the SharpTS compiler host.

The CI cohort runs on matching Windows, Linux and macOS x64/ARM64 runners:

- `language`: loops, numeric functions, callbacks, arrays and JSON.
- `arguments`: spaced arguments and a nonzero exit code.
- `modules`: an imported function and module initialization.
- `generators`: generator creation and iteration.
- `exceptions`: guest object identity/properties across a catch.
- `bcl`: direct immutable CLR constructor, member and property access, plus
  `RuntimeFeature.IsDynamicCodeSupported == false` in the executed guest.

Run that cohort after building the managed compiler:

```powershell
dotnet build src/SharpTS/SharpTS.csproj -c Release
./scripts/test-native-guest.ps1 -RuntimeIdentifier win-x64 -Cases @('language', 'arguments', 'modules', 'generators', 'exceptions', 'bcl')
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

The runner's default selection additionally includes the **candidate** `collections`
and `async` fixtures. They currently fail strict publication on remaining
constructor reflection analysis, tracked in
[#1351](https://github.com/nickna/SharpTS/issues/1351). They are retained for
graduation testing and must reach zero findings plus correct native execution
before joining CI's supported cohort. The CLI preserves the original SDK
diagnostics and promotes no failed output. This cohort does not complete the
epic's class, third-party closure, networking, crypto, P/Invoke, SDK platform,
benchmark or native debugger acceptance work.

The recorded elapsed times are test timings, including process/log overhead.
They do not replace the release performance benchmark. Native symbol files and
guest portable PDBs do not establish TypeScript stepping in a native debugger.
