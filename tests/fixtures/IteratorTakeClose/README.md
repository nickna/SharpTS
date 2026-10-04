# Iterator take closing — #1747

`original.ts` and `original-direct.ts` preserve both exact issue programs.
Fresh unchanged main prints `1 0` for taking one custom-iterator value, while
the direct-return control already prints `true 1`. Both saved baseline outputs
verify their IL, finish within the original 30 seconds and have empty stderr.

Thirteen compiled references also run as isolated saved standalone outputs.
They cover zero limits, closing on the resume after the final yielded value,
normal exhaustion, generator finally blocks, next failures, return failures,
primitive return results, lazy pipelines and flatMap inner/outer cleanup.
Closed helpers stay completed; inner close errors retain precedence over an
outer close error. Node and TypeScript check each source separately with
`--lib esnext,dom`. Saved execution requires exact stdout, empty stderr,
clean exit within 30 seconds and no SharpTS reference or copied runtime.

The take helper marks completion before advancing or closing and forwards
limit-reached closing through the scoped iterator-close handle. Internal
adapters and lazy helpers forward disposal to their underlying guest iterator;
generators retain guest return-result validation. Normal exhaustion skips
closing. Native reused-emitter tests check receiver close counts, completion,
scoped dependencies and standalone/hosted declaration ownership.

These are bounded compiled behavior checks. Interpreter helper support,
all other helper abrupt-completion policies and hosted guest execution are
outside this repair.
