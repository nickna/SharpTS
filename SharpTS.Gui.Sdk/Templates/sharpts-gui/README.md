# SharpTSGuiApp

This project is a TypeScript/TSX Avalonia desktop application; it contains no C# or AXAML.

```powershell
dotnet restore
dotnet build
dotnet run -- --mode interpreted
dotnet run -- --mode compiled
```

Run the included Headless smoke test in either guest mode:

```powershell
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode interpreted --headless
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode compiled --headless
```

Framework-dependent directory publish retains both modes by default:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:SharpTSGuiPublishMode=Directory
```

Set `SharpTSGuiIncludeSourcePayload` to `false` for a compiled-only directory. Files under
`Assets` are embedded and available through stable `asset:///...` URIs.
