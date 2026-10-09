# Compiled debugger acceptance fixture

`main.ts` and `helper.ts` cover the compiled TypeScript breakpoint and stepping checklist.
Comments mark expected source lines for the [VS Code acceptance runner](../../../scripts/editor-smoke/compiled-debugger/README.md).
The runner compiles fresh debug and non-debug outputs and tests the real C# adapter.

Expected output:

```text
first 11
second 12
done 11
async 12
completed 12
```
