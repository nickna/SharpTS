# Dynamic generator iterator — #1729

`lookup.ts`, `original.ts`, `captured.ts` and `next-control.ts` preserve the
issue sources and Node expectations. Fresh unchanged main (`0ad37b57`)
IL-verifies the original and captured programs but throws the reported missing
iterator TypeErrors. The public-next control still prints `1 false 2 false 9 true`.
These fresh failures exit within the original 30-second limit; the issue's
historical 30-second timeout remains a separate observation. No historical
process-shutdown cause or timeout repair is inferred.

Dynamic compiled symbol lookup now falls back to the generator's existing
iterator interface method and exposes it through the ordinary callable
wrapper. The interpreter exposes the corresponding built-in iterator. Direct
calls and captured call/apply/bind with the generator receiver return that
generator, preserving sent values, completion, return/finally and thrown
identity. The seven fixtures pass Node and TypeScript (explicit non-strict
settings retain the original unannotated generator sources).

Shared tests verify both engines and compiled IL; isolated saved tests verify
all seven standalone outputs, absence of runtime references/copies, exact
stdout, zero exit and empty stderr within 30 seconds. Native generator,
iterator protocol, object-read ownership, optional metadata, emitter reuse and
hosted declaration checks remain covered. No generic rebinding to unrelated
receivers, intrinsic method metadata/identity, async-generator symbol lookup,
or hosted guest execution repair is claimed.
