# Indirect eval deployment (#1771)

`original.ts` reproduces the source printed in the frozen issue body. Normal
compilation with `--no-tsconfig --compile original.ts -o main.dll --verify`
must deploy the matching optional SharpTS runtime and print `3`, with verified
IL, empty stderr and clean exit within 30 seconds. The output must have no
hard SharpTS assembly reference. The issue's historical source hash is retained
in the frozen issue; this reproduction is reconstructed from its displayed text.

Fresh unchanged main and pre-repair current both omit SharpTS.dll and report
the missing interpreter bridge, with empty stdout. Both fresh diagnostic
processes exit within 30 seconds; the historical Windows deadline remains a
separate observation owned by #1772.

The remaining sources cover direct dynamic eval, first-class aliases, named,
computed and dynamic-key global reads, generators, async functions and async
arrows. Node and TypeScript check the reference sources independently. Direct
static eval still reads caller locals. Explicit standalone non-string eval
returns the original object and number without a runtime copy. The standalone
missing-bridge source intentionally catches the diagnostic and prints `true`;
Node has its own eval runtime and prints `3` for that source.

Acquiring eval as a value conservatively records the optional bridge requirement
because a later alias call can supply dynamic source. Normal mode therefore
also deploys it for value-form uses that happen to receive only non-strings;
explicit standalone mode retains runtime omission and the capability warning.
No general dynamic direct-eval lexical-scope or process-lifetime fix is claimed.
