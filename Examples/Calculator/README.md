# SharpTS Calculator

A polished `SharpTS.Gui.Sdk` showcase. It uses typed
function components, keyed data-driven rendering, retained native controls, accessible names,
responsive sizing, keyboard input, and a window-level error boundary without adding history,
memory, or scientific modes.

## Run it

After `SharpTS.Gui.Sdk` is available from a configured feed:

```powershell
dotnet run --project Examples/Calculator -- --mode compiled
dotnet run --project Examples/Calculator -- --mode interpreted
dotnet publish Examples/Calculator/Calculator.csproj -c Release -r win-x64
```

Within the SharpTS repository, `./Examples/Calculator/run-local.ps1` packs the customer-facing
SDK into a local feed before restoring and launching this unchanged project. Use
`-Mode interpreted` for the source guest or `-Headless` for a short non-interactive smoke run.

## Controls

- Click the labeled buttons or use `0`–`9`, `.`, `+`, `-`, `*`/`X`, `/`, `%`, `Enter`/`=`,
  `Backspace`/`Delete`, and `C`/`Escape`.
- `C` resets entry, active operations, and errors. `=` repeats the last completed operation.
- The expression line shows the pending or completed calculation. The status line explains the
  next action and gives a clear divide-by-zero recovery hint.
- Every control has a semantic automation name; button tooltips include keyboard shortcuts.

## Architecture highlights

The calculator reducer and presentation derivation live in `calculator.ts` and have no GUI
dependency. `CalculatorApp.tsx` renders typed button definitions through keyed
`CalculatorButton` components, exercising component-key reconciliation while native Button
instances and event subscriptions remain stable across updates.

The app demonstrates meaningful uses of `useReducer`, `useMemo`, `useCallback`, `useEffect`, and
a typed Window control ref. Event callbacks are regenerated from current state while the retained
renderer dispatches through the latest callback without duplicating native subscriptions.

`CalculatorShowcase` wraps the complete Window in an `ErrorBoundary`. If rendering fails, the
process stays alive and a friendly recovery Window offers **Retry** instead of silently closing.

## Verification

The framework-independent reducer suite covers arithmetic, percent, repeat-equals, digit limits,
clear/error recovery, expression/status text, and keyboard-equivalent actions:

```powershell
dotnet bin/Debug/net10.0/SharpTS.dll Examples/Calculator/calculator.tests.ts
```

The GUI conformance project builds a dedicated test guest and runs the same interaction script in
Avalonia Headless under interpreted and compiled SharpTS. It verifies `12 + 3 =`, clear during an
active calculation, divide-by-zero recovery, mouse/keyboard parity, active-operator styling,
expression text, stable keyed identities/subscriptions, and error-boundary retry:

```powershell
dotnet test SharpTS.Gui.Conformance.Tests --filter CalculatorHeadlessTests
```
