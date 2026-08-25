# SharpTS Calculator

A multi-mode `SharpTS.Gui.Sdk` showcase modeled after Windows Calculator. It combines exact
decimal arithmetic with scientific expressions, fixed-width programmer operations, calendar
math, converters, graphing, history, memory, keyboard input, and a compact always-on-top view.

## Run it

After `SharpTS.Gui.Sdk` is available from a configured feed:

```powershell
dotnet run --project samples/Calculator -- --mode compiled
dotnet run --project samples/Calculator -- --mode interpreted
dotnet publish samples/Calculator/Calculator.csproj -c Release -r win-x64
```

Within the SharpTS repository, `./samples/Calculator/run-local.ps1` packs the customer-facing
SDK into a local feed before restoring and launching this unchanged project. Use
`-Mode interpreted` for the source guest or `-Headless` for a short non-interactive smoke run.

## Modes and controls

- **Standard** uses rational arithmetic for exact decimal entry, repeat-equals, percent, memory,
  reciprocal, square, square root, clear-entry, and the familiar keyboard shortcuts.
- **Scientific** evaluates precedence-aware expressions with parentheses, powers, factorials,
  logarithms, trigonometry, constants, DEG/RAD/GRAD, and fixed/scientific notation.
- **Programmer** provides BIN/OCT/DEC/HEX readouts, 8/16/32/64-bit wrapping, gated digits,
  arithmetic, bitwise operations, shifts, rotates, NOT, and bit toggling.
- **Date Calculation** finds day and calendar differences and adds or subtracts calendar
  durations with end-of-month clamping.
- **Converter** covers length, mass, temperature, area, volume, speed, time, energy, power,
  pressure, angle, data, and currency. Currency uses clearly labeled deterministic offline rates;
  the model exposes an injectable provider interface for a live source.
- **Graphing** plots multiple expressions, toggles series, zooms/resets the viewport, and traces
  coordinates. History is shared between calculation modes, and Standard can switch into a
  compact native always-on-top window.

Every interactive control has a semantic automation name. Standard keyboard shortcuts include
`0`–`9`, `.`, `+`, `-`, `*`/`X`, `/`, `%`, `Enter`/`=`, `Backspace`, `Delete` (CE), `Escape`
(clear), `F9` (sign), `R` (reciprocal), `Q` (square), and `@` (square root).

## Architecture highlights

The domain is split into GUI-independent modules: `calculator.ts`, `exact.ts`, `expression.ts`,
`programmer.ts`, `dateCalculation.ts`, `converters.ts`, and `graphing.ts`. `CalculatorApp.tsx`
composes them through typed, keyed components while retained native controls and event
subscriptions remain stable across updates.

The app demonstrates meaningful uses of `useReducer`, `useMemo`, `useCallback`, `useEffect`, and
a typed Window control ref. Event callbacks are regenerated from current state while the retained
renderer dispatches through the latest callback without duplicating native subscriptions.

`CalculatorShowcase` wraps the complete Window in an `ErrorBoundary`. If rendering fails, the
process stays alive and a friendly recovery Window offers **Retry** instead of silently closing.

## Verification

The framework-independent suite covers exact arithmetic, Standard behavior, scientific parsing,
programmer word semantics, calendar edge cases, conversion formulas, and graph sampling:

```powershell
dotnet src/SharpTS/bin/Debug/net10.0/SharpTS.dll samples/Calculator/calculator.tests.ts
```

`programmer.tests.ts` is also compiled to verified IL by the development workflow to guard the
BigInt-based fixed-width path.

The GUI conformance project builds a dedicated test guest and runs the same interaction script in
Avalonia Headless under interpreted and compiled SharpTS. It exercises every mode, including
scientific precedence, programmer shifts, date defaults, unit and currency conversion, graph
trace/zoom, always-on-top state, Standard mouse/keyboard parity, and error recovery:

```powershell
dotnet test tests/gui-conformance/SharpTS.Gui.Conformance.Tests --filter CalculatorHeadlessTests
```
