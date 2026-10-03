# Async delegation completion — #1733

The five original issue sources remain unchanged: async/sync aggregates,
async/sync direct completion records, and the passing values-only control.
Fresh unchanged main (`0ad37b57`) IL-verifies and exits normally with empty
stderr within the original 30-second deadline. Both aggregates print `NaN`;
both direct records end with `undefined true` instead of `4 true`. The
values-only control prints `5` correctly.

Compiled async delegation reads the delegate's final result value. Synchronous
generator delegation drives the existing public next(v) protocol to preserve
sent values and completion; ordinary CLR iterables still complete with the
undefined sentinel. Synchronous delegate errors use the existing guest
catch/finally capture path. The interpreter shares async/sync generator driving
and preserves explicit null completion values.

Fourteen fixtures run in both engines and as standalone saved output, covering
the exact originals, sent values, natural and error cleanup, awaiting cleanup,
empty completion, nullish/falsy/object identity, and nested delegation to a
genuinely pending async generator. A failure clears active delegation before
the outer catch suspends at an ordinary yield, preserving later throw cleanup.
Saved tests require verified IL, absence of
runtime references/copies, exact Node stdout, zero exit and empty stderr
within 30 seconds. All sixteen references pass Node and TypeScript.

Two independent fixtures retain unawaited async-generator return promises.
Even without delegation, unchanged main and current compiled output expose
the Task itself and interpreted output exposes the Promise instead of `9`.
The empty passing control explicitly awaits its return promise; its earlier
unawaited version remains unchanged as evidence outside passing counts.
No general return-promise adoption, external delegated return/throw protocol,
custom async iterator adapter, compiled async string iteration, or hosted
guest execution repair is claimed.
