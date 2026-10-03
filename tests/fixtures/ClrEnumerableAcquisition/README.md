# Native CLR enumerable acquisition — #1754

This probe preserves the unchanged raw LINQ failure and initialized-enumerator
control. It saves and IL-verifies six generated outputs per compiler, checks
standalone/hosted deployment references and independent owners, and prints
expected/observed values with output hashes. `OriginalMatchesExpected: false`
records the failure; exit zero means the probe and independent controls ran.

Run against a built compiler, using an absolute compiler path when selecting a
different revision:

```powershell
dotnet run --project tests/fixtures/ClrEnumerableAcquisition/ClrEnumerableAcquisition.csproj -c Release -p:SharpTsCompilerPath=C:/path/to/SharpTS.dll -- artifacts/clr-acquisition
```

Fresh unchanged main and current both return an empty array from the raw LINQ
input, including repeated use. Explicit initialized/active/exhausted enumerator
and Queue controls pass. Enumerable-only and enumerator-only role adapters
demonstrate correct acquisition and cursor consumption through public CLR
interfaces. They do not repair or replace the original input.

The finite acquisition contract is linked in
[clr-enumerable-acquisition.md](../../../docs/plans/clr-enumerable-acquisition.md).
No production change, guest failure, hosted guest execution, or binding/closing
repair is claimed.
