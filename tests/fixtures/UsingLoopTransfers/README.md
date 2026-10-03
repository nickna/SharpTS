# Ordinary using loop transfers (#1786)

`loop_control.ts` and `loop_finally_control.ts` preserve the displayed original
sources and Node output `0`, `1`, `after`. Fresh unchanged main rejects the using
source with BranchOutOfTry at offsets 136 and 164; pre-repair current rejects it
at 243 and 271 after the intervening acquisition repair. Neither invalid assembly
is executed. The explicit-finally control already verifies and passes.

Twelve TypeScript/Node references cover both transfers, multiple declarations,
LIFO cleanup, nested/labeled loops, loops wholly inside a using scope, while/do/
for-of forms, nested block returns, captured methods, unreached declarations,
later acquisition failures and disposal errors that cancel a pending transfer.
TypeScript 7.0.2 and Node 25.5.0 establish each expectation before SharpTS runs.

Using blocks now track their protected region through the existing IL builder
and context, so exits use leave when required. Block and function statement lists
share that path. Cleanup retires registration flags before invoking user code,
preventing resources from an earlier iteration being disposed again when a later
declaration is not reached. The acquisition-failure control demonstrates stale
cleanup on both fresh unchanged main and pre-repair current; current matches Node.

Saved checks require verified IL, exact stdout, empty stderr, no SharpTS
reference/copy and clean exit within the unchanged 30-second limit. Hosted checks
verify declarations without invoking exports. Existing emitted disposal metadata
ownership and helper signatures remain unchanged; no ownership audit closure is
claimed.
