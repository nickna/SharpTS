# Unary numeric representation

Unary `-` and `~` can produce a BigInt even when TypeScript checks the result as
`number`, including through `any` assertions and object-to-primitive callbacks.
The compiler inventories those actual source operations before lifting and keeps
their Number-or-BigInt result facts across equivalent rewrites, storage, returns
and source argument transport. The existing boxed unary emitter evaluates the
operand once and applies `ToNumeric` once.

These facts only prevent representation narrowing. They grant no stable callee,
native owner or reflection permission. An unknown argument target conservatively
retains object storage across the actual emitted source parameter inventory;
explicit CLR signatures remain fixed. Proven primitive Number unary expressions
retain their previous path. Programs with an uncertain unary producer can box a
wider source parameter cohort to preserve JavaScript values.

The unchanged original comparison, local storage, typed forwarding, coercion
order, numeric assertions and suspension controls have pinned TypeScript/Node
oracles and saved-IL checks. The interpreter's independent object-coercion defect
is tracked by #3145.
