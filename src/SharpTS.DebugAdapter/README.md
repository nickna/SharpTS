# SharpTS Debug Adapter

`sharpts-dap` is the client-independent Debug Adapter Protocol server for interpreted SharpTS
programs. It uses DAP framing over stdin/stdout; stdout must not be used for logs.

Most users start it through the SharpTS VS Code extension. Other DAP clients can launch the tool
and send a `launch` request whose `program` property names a saved TypeScript entry file. See
[`docs/debugging-interpreter.md`](../../docs/debugging-interpreter.md) for configuration, security,
capabilities, and limitations.

Install with `dotnet tool install --global SharpTS.DebugAdapter`; use `sharpts-dap --version` to
confirm the adapter and client bundle are from the expected release.
