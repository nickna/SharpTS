# CLR enumerable acquisition — #1754

## Evidence and disposition

This is a finite design transfer, not a behavior repair. The unchanged original
LINQ input and its initialized-enumerator control are preserved in the native
probe at `tests/fixtures/ClrEnumerableAcquisition`. Fresh unchanged main
(`0ad37b574e3c41d267300ada003d63d0f8026562`) and revision `569e88fd` reproduce the
same failure in six configurations each: minimal first, array, optional
collections, array mutation, minimal again and hosted emission. Every generated
output is saved, IL-verified and checked for deployment-reference isolation.
Standalone outputs have no SharpTS reference; only hosted outputs reference
SharpTS.Hosting.Abstractions. Compiler and output hashes are captured by the
probe procedure. No guest-source failure is established by this native evidence.

| Native input | Required values | Both compiler versions |
| --- | --- | --- |
| Original raw Range/Select enumerable | `0, 1, 2` | empty |
| Original explicit GetEnumerator control | `0, 1, 2` | `0, 1, 2` |
| Enumerator advanced once | `1, 2` | `1, 2` |
| Exhausted enumerator | empty | empty |
| Same raw enumerable, first and second use | `0, 1, 2` each | empty each |
| Queue control | `0, 1, 2` | `0, 1, 2` |
| Enumerable-only role adapter, two uses | `0, 1, 2` each | `0, 1, 2` each |
| Enumerator-only role adapter, active/completed | `1, 2` / empty | `1, 2` / empty |

The original failure remains outside passing counts. The native probe's clean
exit establishes successful evidence collection and passing independent
controls; `OriginalMatchesExpected: false` explicitly records the unresolved
behavior. Hosted verification proves generated declarations and references,
not hosted guest execution.

## Acquisition intent

The raw LINQ value implements both IEnumerable and IEnumerator. The current
collector selects IEnumerator first and calls MoveNext before the fresh LINQ
sequence has been initialized by GetEnumerator. Reversing this priority would
produce the original expected values, but would restart an already active LINQ
enumerator and make an exhausted enumerator yield again. Existing lazy helpers
must also retain their cursor and completion state.

An object-typed native boundary erases whether the caller supplied a sequence
to enumerate or a cursor to resume. Neither interface membership nor generic
element type supplies that intent. A type-name whitelist, inspection of private
LINQ state, guessing from Current, or advancing a cursor to determine its role
would couple behavior to implementation details or consume observable state.
GetEnumerator reference identity and an inferred per-object role cache also
need a contract for repeat use, thread changes and native implementations that
return a new enumerator in both fresh and active states. They are not a general
replacement for acquisition intent.

The retained role-adapter controls demonstrate the distinction using public
CLR interfaces: an enumerable-only wrapper acquires a cursor on each use; an
enumerator-only wrapper consumes the supplied cursor. This is executable design
evidence, not a production workaround for the unchanged original.

## Implementation boundary

1. Carry an explicit enumerable/iterator role at native interop boundaries before
   a value is erased to object. Inspect reflected parameter/return types at those
   boundaries; preserve explicit IEnumerator values as cursors and IEnumerable
   values as sequences. Ambiguous object-typed values require a documented host
   API or adapter that chooses a role.
2. Declare the role adapter or acquisition operation in the iterator collection
   owner with scoped inputs. Keep the existing public normalizer signature and
   emitted ownership contracts intact; expose any additional explicit native
   operation separately. Decide and document the legacy ambiguous-input policy
   before changing the generic collector's interface priority.
3. For sequence intent, invoke GetEnumerator exactly once per collection and
   consume that returned cursor. Repeated collection acquires another cursor.
   For cursor intent, consume the supplied cursor without invoking its
   GetEnumerator or restarting completion. Guest Symbol.iterator selection,
   captured next, error precedence and native helper forwarding retain their
   existing paths.
4. Audit standalone and hosted native invocation, optional collections and all
   consumers of IterateToList/IterateIntoList/NormalizeToEnumerator. A global
   interface-order swap must not become the implicit policy for unrelated guest
   iterators. Define disposal ownership for newly acquired native cursors.

The implementation must explicitly reconcile the unchanged object-typed
original and initialized control with the chosen legacy policy. Adding a role
API alone does not repair those original calls and must not be reported as such.

## Finite acceptance and stopping conditions

- Preserve the exact original Range/Select and initialized control, plus the
  existing active/exhausted controls. Record any legacy-policy change explicitly;
  do not rewrite the failing original as an explicit GetEnumerator input.
- Verify repeated sequence acquisition and single acquisition per use, active
  and exhausted cursor consumption, null-valued elements, acquisition/next
  failures, iterator helpers, and role isolation across generated outputs.
- Repeat all six native configurations with verified saved IL, expected values,
  deployment references, independent declaration owners and empty diagnostics.
  Run affected compiler tests, quality gates and the actual AOT baseline.
- Stop when this acquisition contract and its native cases are implemented.
  Do not claim guest compatibility without a guest reproduction, alter #1750
  binding/closing semantics, or reopen adjacent ownership/conformance work.

This linked plan is the #1754 outcome included in the consolidated epic PR.
No separate successor issue has been published.
