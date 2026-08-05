# Native AOT host with custom .NET interop

This is the repository smoke fixture and a copyable host template. The project
declares a closed interop type set with `SharpTSNativeInteropType`, publishes a
Native AOT executable, and starts the ordinary SharpTS CLI with the generated
catalog.

For an installed package, replace the repository imports/project reference with:

```xml
<PackageReference Include="SharpTS.Hosting" Version="..." />
```

Keep interop implementations in class libraries. Set each item's `Assembly`
metadata to that library's assembly name; the build embeds its managed DLL and
copy-local dependency closure so `--compile` can deploy them beside its output.

```powershell
dotnet publish -c Release -r win-x64
./bin/Release/net10.0/win-x64/publish/NativeInteropHost.exe custom-interop.ts
```
