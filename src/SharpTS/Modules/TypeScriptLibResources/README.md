# TypeScript standard library declarations

These files are copied verbatim from the TypeScript 6.0.3 distribution in
`external/typescript/lib` and embedded in SharpTS so normal builds do not require
the conformance-test submodule.

TypeScript is copyright Microsoft Corporation and licensed under Apache-2.0.
See `LICENSE.txt` in this directory.

`SHA256SUMS` records the exact upstream license and declaration hashes. To update
or verify the bundle:

```powershell
./scripts/sync-typescript-libs.ps1 -Update
git add external/typescript src/SharpTS/Modules/TypeScriptLibResources src/SharpTS/Modules/TypeScriptLibProvider.cs
./scripts/sync-typescript-libs.ps1
```

The check verifies the staged submodule gitlink, its exact version tag,
`TypeScriptLibProvider.CompilerVersion`, the complete file set, the upstream
license, and every hash. The script requires the submodule, but normal
builds and release packages use only these embedded resources.
