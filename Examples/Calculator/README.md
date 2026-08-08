# SharpTS Calculator

A complete TSX desktop calculator demonstrating typed function components, React-style hooks,
natural text children, retained updates, keyboard input, focus refs, and direct typed styling.

After `SharpTS.Gui.Sdk 0.1.0-preview.1` is available from a configured feed:

```powershell
dotnet run --project Examples/Calculator -- --mode compiled
dotnet publish Examples/Calculator/Calculator.csproj -c Release -r win-x64
```

Within the SharpTS repository, run `./Examples/Calculator/run-local.ps1`; it packs the same SDK
consumed by customers into a local feed before restoring and launching this unchanged project.
Pass `-Mode interpreted` to exercise the source guest, or `-Headless` for a short automated smoke
run. The framework-independent reducer contract is covered by `calculator.tests.ts`.
