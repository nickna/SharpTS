# Awaited for-await elements — #1735

The four original programs remain unchanged: the array and async-generator
failures, plus their any-typed controls. Fresh unchanged main (`0ad37b57`)
rejects the originals with the reported numeric-operation diagnostics and
produces no output assembly. Both controls IL-verify and print `6` / `7` with
clean exits and empty stderr within the original 30-second deadline.

For-await binding now uses the same recursive Promise unwrapping as await,
distributing over element unions. Ordinary for-of still binds Promise values;
awaiting a tuple entry does not recursively await its nested members.
Eight runtime fixtures run in both engines and as standalone saved outputs,
including mixed/readonly array unions, a synchronous generator and a Set.
Saved tests require verified IL, absence of runtime references/copies, exact
Node stdout, zero exit and empty stderr within 30 seconds.

Five additional declared-type controls check recursive Promise elements,
nominal sync/async iterables, ordinary for-of and tuple-member preservation.
Their functions are deliberately uncalled: these are static inference checks,
not runtime protocol comparisons. Seven explicit declared-type negative
bindings fail in both test-harness modes and in the CLI, which must produce
no assembly. The declarations also avoid the bare test-harness checker's
permissive inference for Promise.resolve calls. TypeScript accepts all thirteen
positive references and rejects all seven negative references.

Two separate primitive-member fixtures are excluded from passing counts.
TypeScript rejects number.then, while unchanged main and current SharpTS
accept the direct declared-number member control even without a loop. The
awaited member version retains the observed miss as well. No general primitive
member validation, arbitrary thenable/generic Awaited expansion, non-iterable
async source diagnosis, or hosted guest execution repair is claimed.
