# Dynamic custom iterator implementation results

Implementation of [the iterator performance plan](custom-iterator-dynamic-control.md).

## Changes

- Capture the iterator's `next` value once during acquisition in compiled
  `for...of`, the generic iterator wrapper, the async-from-sync adapter, and the
  interpreter. Later mutations remain visible to explicit property accesses and
  subsequent acquisitions. Calls retain the original receiver.
- Dispatch admitted zero-argument methods through the fixed-arity
  `MethodInvoker.Invoke(target, receiver)` overload. Cached function metadata
  excludes argument capture, parameter adjustment, and conversion from this path;
  other callables use existing dispatch with `Array.Empty<object>()`.
- Represent numeric `{ value, done }` results with the existing compact reference
  record infrastructure, including when results escape. Each call still returns
  a fresh ordinary object. Reads guard materialization and descriptor overlays
  before accessing fields; unknown shapes use ordinary property access.
- Prove numeric function-owned closure fields independently of stable iterator
  eligibility. The initial proof admits a single capturing callable, resolved
  ownership, unambiguous names, definite numeric initialization, and numeric
  writes. Ambiguous bindings, additional capturing callables, eval, suspension,
  and undefined-reachable values retain existing storage.
- Bind interpreted iterator methods once and avoid constructing intrinsic
  function metadata that a bound view immediately replaces with shared storage.
- Preserve lexical arrow receivers during compiled dictionary property lookup,
  and pad omitted parameters before a rest parameter with the same undefined
  policy as ordinary calls. Regression tests found both issues in the baseline.

The dynamic benchmark retains its alias and `alias.next = alias.next` statement.
Its compiled loop still calls the generic iterator protocol. Numeric result
representation does not authorize stable iterator specialization or numeric
checksum accumulation; the consumer retains dynamic addition.

## Measurement method

Baseline: `88378dce04627da968dfde8243c4585b45ed3dfa`. A copy of its Release compiler
and runtime binaries was frozen before source changes. Both compilers use the
same final benchmark sources, differing from the originals only by the expected
checksum passed to `bench`. No shared timing configuration or published snapshot
was changed.

Environment: Windows x64, .NET SDK 10.0.400 / runtime 10.0.11, Node 22.23.2.
Full-workload comparisons use five process launches, alternating baseline and
candidate order, with the original imported timing driver. Each launch emits
both existing problem sizes and checks the checksum. Node is a reference using
the same sources. Tests and builds do not run concurrently with timed samples.

The new BenchmarkDotNet classes compile the original modules and call their
workload functions using cached delegates. The interpreter attribution cases
execute their original function ASTs without the timing driver. Direct `next`
and ordinary-call controls deliberately omit parts of the complete workload.
Allocation probes use a separate three-second warmup and report managed bytes
and Gen0 counts. Results from these routes must not be substituted for the
cross-runtime timing driver.

## Remaining work

Interpreter environment reuse is deferred. The current change removes repeated
binding and discarded intrinsic metadata, while function calls, result objects,
and per-iteration lexical environments still allocate. Reusing environments
requires a separate lifetime proof covering retained closures, eval, and debugger
observation. The plan explicitly allows this evidence-based deferral.

Compiled result objects still allocate, and generic value access and checksum
addition still box numbers. Removing those costs requires further guarded
specialization that preserves result identity, string concatenation, coercion,
and mutation. The ordinary generic-call control is intentionally unchanged.
