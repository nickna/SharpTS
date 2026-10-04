# Runtime enum value bindings (#1790)

The eight named original cases preserve the displayed issue sources and Node
expectations. Fresh unchanged main and pre-repair current verify but fail
computed members, aliases and generator access. Fresh failures complete within
30 seconds; the historical hangs are not claimed as reproduced. The overlapping
negative-value control already passes after #1788.

Twelve script and two module references were established with TypeScript 7.0.2
and Node 25.5.0 before their SharpTS runs. Controls cover initializer evaluation
order, bare previous-member references, duplicate values, string members,
namespace/module alias identity and generator references across suspension.

Ordinary enum declarations now initialize shared runtime objects in source order.
Aliases and module exports retain that object; synchronous and suspended bodies
resolve the same value binding. Const-enum inlining remains covered separately.
Existing native enum helper ownership and its direct missing-key exception
contract remain unchanged. Guest missing properties use ordinary object lookup.

Saved standalone outputs require verified IL, exact stdout, empty stderr, clean
exit and no SharpTS reference/copy within the original 30 seconds. Hosted checks
verify declarations and deployment without executing exported functions. The
outcome record gives the selected regression and analyzer results.

Final integration controls execute hosted top-level-await modules. Their enum
declarations run in source order on either side of suspension, and exported
objects retain alias identity without repeating initializer side effects.
TypeScript/Node references precede the saved hosted IL verification and runtime
checks; these additional controls extend the earlier declaration-only evidence.
