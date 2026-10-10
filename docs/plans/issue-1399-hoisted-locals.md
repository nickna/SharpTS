# #1399: hoisted source variables in managed debuggers

Issue: [#1399](https://github.com/nickna/SharpTS/issues/1399).
Investigation base: `5256a80b` (#1984). This work concerns compiled TypeScript.

## Investigation and accepted target

The original compiled-debugger acceptance fixture and an equivalent
[Roslyn control](../../tests/fixtures/HoistedDebuggerAcceptance/roslyn/Program.cs)
were run before changing the debugger-facing type and field conventions. The
control uses genuine timer suspension, an iterator, an async iterator, a shared
capture, and a block local. Its eight source stops check values and visibility.

The accepted primary target is the VS Code C# `coreclr` debugger, through its
supported VS Code client. The verified host is Windows x64, VS Code 1.140.0,
C# extension 2.140.9, .NET runtime extension 3.2.0, and .NET SDK 10.0.401.
`vsdbg` was never launched outside that client.

The secondary comparison uses the official Samsung netcoredbg release
[3.2.0-1092](https://github.com/Samsung/netcoredbg/releases/tag/3.2.0-1092), whose
binary reports `NET Core debugger 3.2.0-1 (9744e1f, Release)`. Visual Studio
Professional 2026 Insiders 18.11.12210.170 is installed on this host, but native
editor automation is unavailable; its presentation is not certified by these
checks. Rider is not installed.

| Debugger | Unchanged SharpTS | Roslyn control | Accepted SharpTS behavior |
|---|---|---|---|
| VS Code C# | Source breakpoints bind, but async/iterator frames are `<Unknown function>`; requesting Locals or bare source Watches returns an expression-evaluator internal error. | All eight source stops provide expected source variables, values, and block visibility. | Recognized source frames, source-named Locals and Watch values, lexical scope exit, and correct shadow resolution. |
| netcoredbg | Bare source-named fields are already offered as variables; unnamed generated entries can appear. | All eight stops provide expected values and scope visibility. | Source Watch values and scope visibility agree with the primary target, with no unnamed entries or compiler scaffolding. Seven shadowing stops retain duplicate names in Locals; boxed numeric values require expanding `m_value`. |

The first primary candidate passed 44 strict source stops in
`artifacts/hoisted-investigation/scopes-candidate/run-OmUxZS`. The same assembly
passed all 44 secondary Watch/value and out-of-scope checks. This early result
does not substitute for the final-source acceptance record linked below.

The final #1985 primary run passed 55 stops and 123 expected binding observations.
Each expected binding appears exactly once in Locals with its correct value,
and Watch agrees. It includes free functions, methods, namespaces, async arrows,
true suspension, captures, shadows, loops, catches, imported source, and scope
exit. The final secondary comparison uses the same compiler build and fixture;
the same assembly also passes all 55 secondary stops and 123 first-Locals value
checks. The original 12-stop stepping check also passes. The expanded program has
identical output with and without symbols and passes IL verification.

The secondary's early unnamed entries came from its generated-field parser:
wrapping a prefix such as `<>5__executing` inside another generated name left a
`>5` substring that it treated as a hoisted source field. Normalizing that prefix
removes the ambiguity. The final run has no unnamed fields; this was not a
portable-PDB scratch-local filtering failure.

## What the debuggers consume

The portable-PDB
[StateMachineHoistedLocalScopes specification](https://github.com/dotnet/runtime/blob/main/docs/design/specs/PortablePdb-Metadata.md#state-machine-hoisted-local-scopes-c--vb-compilers)
defines an ordered sequence of little-endian `(start offset, length)` pairs on
the actual `MoveNext` MethodDef. An unused slot has `(0, 0)`. The blob contains
no field handles; source variable discovery also depends on generated names.

The primary expression evaluator locates the kickoff method by parsing the
state-machine type name and searching its containing type. SharpTS's original
standalone `<source-name>d__N` types did not encode the actual emitted kickoff
method or its declaring type. Nesting under the actual kickoff owner and using
the actual metadata method name fixed recognition. Dots in namespaced method
names use the generated-name escape consumed by Roslyn.

Roslyn's
[expression compiler](https://github.com/dotnet/roslyn/blob/main/src/ExpressionEvaluator/CSharp/Source/ExpressionCompiler/CompilationContext.cs)
recognizes `<source>5__N` hoisted fields and `<>8__N` display-class holders, and
uses the corresponding PDB slot's range. Field order matters when several live
fields have the same source name. Both the generated conventions and the scope
records were checked through the live debuggers; adding the custom information
GUID alone would not have fixed the original evaluator failure.

## Implementation

Debug compilation carries a binding catalog with declaration identity, source
name, lowering storage key, lexical scope, emitted field, and hoisted slot.
Source tokens distinguish user declarations from synthetic lowering variables.
Inner bindings are ordered before outer bindings. Sibling, loop, and catch
bindings retain distinct storage keys where the lowering supports that identity.

Hoisted fields use source names in the recognized generated convention. Shared
captures project their authoritative display-class storage; stale machine
mirrors are hidden and their slots disabled. Compiler infrastructure uses a
non-user generated-name kind. Actual IL scratch slots receive explicitly hidden
PDB local rows using the final PE's local-signature counts.

Scope ranges come from the emitted statements belonging to each lexical scope.
Control-header sequence-point ranges end before the body, so a body breakpoint
binds after its bindings have entered scope. Existing kickoff mappings, async
stepping records, generated-code attributes, source checksums, and final rewritten
PE/PDB identities remain covered by the original and extended metadata tests.

The debugger-facing names, nesting, binding catalogs, and custom information are
enabled only with symbols. Nondebug state machines keep their existing naming
conventions and receive none of the projection catalogs or custom information.
The runtime correctness fixes below apply to both build modes.

A runtime bug uncovered by the suspension checks is fixed in both modes: catch
variables needed after a suspending await must be hoisted, and a generator's
cloned catch node must retain its resolved storage identity. Otherwise the catch
value is reset or overwrites a shadowed outer binding. Regressions require the
thrown value `7` and the restored outer value `99` in both build modes.

Readonly closures over a shadowed mutable binding also use authoritative shared
storage in both modes. A closure created when its binding is `98` must observe
the later increment to `99`, including after suspension. Projecting a correct
debugger value while normal execution uses an obsolete snapshot would violate
the runtime parity requirement.

Loop bindings preserve their existing per-iteration capture storage. Promoting
all readonly shadows into one function display class would merge distinct loop
captures and can also invalidate numeric IL. The binding-aware selection retains
snapshots for loop initializers and never-reassigned, directly captured `let` and
`const` loop-body declarations. Runtime tests check independent values `0/1/2`,
their sum `3`, and restoration of the outer value `7` in both build modes. The
live fixture observes all three loop iterations and the restored outer binding.

Canonical lexical capture information also restores an immediate parent arrow's
`const` captured by a grandchild async arrow. The suspension-only visitor had
omitted that binding, producing `null` in both existing build modes. Runtime reads
now retain the actual parent field. Debug builds give its immutable value a
source-named field refreshed before `MoveNext` dispatch; the source value and
field type remain the same. The two additional debugger stops check the ancestor
captures, parent constant, and child's own parameter/local together.

## Original #1985 reproduction and validation

The [runner guide](../../scripts/editor-smoke/compiled-debugger/README.md)
describes the original stepping acceptance check, the stricter
[hoisted-variable fixture](../../tests/fixtures/HoistedDebuggerAcceptance/main.ts),
and the Roslyn control. Each run records the complete DAP exchange, source stops,
variables, Watches, output, versions, and binary hashes in ignored artifacts.
The [original projection record](../../scripts/editor-smoke/compiled-debugger/hoisted-last-verified.json)
binds the #1985 accepted runs to their source and binary inputs. It remains historical;
the numeric-timer follow-up below has a separate record.

`DebugSymbolsHoistedLocalsTests.cs` structurally decodes every added scope blob,
checks its exact owner and field-slot associations, checks instruction boundaries
and final method sizes, and verifies all named IL slots against final signatures.
It covers suspension families, shadows, loop and catch scopes, shared captures,
destructuring, imported source, `var` lowering, legal user-name prefixes,
deterministic metadata, and nondebug output. Runtime regressions execute both
build modes against explicit expected output.

The final-source focused run passes all 153 metadata, numeric-storage, and async
shadowing tests. The Release solution builds with zero errors, the expanded
fixture passes IL verification after the reference-assembly rewrite, and the
source-quality and emitter-policy checks pass. The build reports 20 existing
NU1900 vulnerability-feed warnings. Avalonia telemetry was disabled for this
workspace build with `UsedAvaloniaProducts=`.
All 1,388 nongenerated SharpTS C# source documents match the accepted compiler's
embedded portable-PDB SHA256 checksums, including every changed compiler file.

The hermetic Release local suite covers 27,704 distinct cases: 27,701 pass and
three are declared skips. Core covers 25,337 cases, GUI covers 134, and standalone
execution covers 2,233 actual data rows. Eight initial core failures have exact
test-name matches that pass in a 53-row sequential retry. The emitter override
contract was updated to allow the new shared binding-storage lookup. Six child
process timeouts and one filesystem callback output failure did not recur;
their recovery does not establish a timing cause.

The standalone runner's initial 30-minute budget interrupted three progressing
shards with zero failures. Targeted continuations exclude all 142 completed
methods, finish the remaining 115 methods, and repeat only 25 rows within three
partial theories. Reflection and the final TRX identity audit verify coverage
of all 257 methods and 2,233 rows, with zero mismatches. Counts above exclude
those repeated rows and the core retry repetitions.

The TypeScript full profile was compared against a separate build of unchanged
`5256a80b`: all 534 case records are identical. Both runs pass 532 cases and fail
the same two committed-baseline comparisons, `checkJsxSubtleSkipContextSensitiveBug.tsx`
(extra TS2339 at line 12) and `variance.ts` (extra TS2345 at line 23). All 16 gate
and harness checks pass. No conformance baselines were changed; these inherited
diagnostic differences prevent claiming that the full profile is green.

The Test262 facts and interpreted-corpus stage covers 3,220 test identities:
3,181 pass, 35 fail, and four are declared skips. Every stable test identity,
outcome, and failure message matches the archived unchanged build. The failures
are 33 existing Issue1279 parity cases, `Diagnostic_NoRegressions`, and
`InterpretedBaseline`. Both interpreted corpus runs execute 11,384 files and
report 9,878 passes; one file changes between the runtime-error and timeout
buckets for the existing `set-cycle-shadowed.js` watchdog classification. Both
report the same four named hard drifts against the committed interpreted
baseline and 59 new passes, with no added or removed paths. Exact equality of
every interpreted file result is not claimed. Two
performance-profiling facts are excluded, and the compiled-corpus fact is
replaced by a single full capture through the same public runner and baseline
comparison APIs, preserving complete path identities for regression attribution.

The full compiled capture executes all 11,384 files: 9,716 pass, 34 fail, 10
produce parse errors, 733 produce runtime errors, five time out, seven produce
harness errors, and 879 are skipped. These counts match the fresh unchanged
build. The committed baseline has 511 differences: 185 regressions, 40 new
passes, and 286 soft bucket changes, with no added or removed paths.

Every one of the candidate's 789 nonpassing paths was compared against the
frozen archived compiler, with exact path and bucket matches and no missing or
extra results. The separate 40 new-pass controls also match. This attributes all
511 current compiled-baseline differences to unchanged HEAD; no introduced hard
regressions were found. The archived run's original bounded logger did not save
every passing file result, so exact full-map equality across all 11,384 files is
not claimed. The existing conformance gates remain red, and no baselines were
updated.

## Follow-up: numeric captures after timer suspension

A numeric loop capture after a genuinely suspending timer await previously produced invalid IL
in both build modes. Suspension hoisted the numeric source binding into an `object` field, while
the readonly closure snapshot used a `double` field. Copying between those storage representations
without conversion caused the verification error. Closure initialization now converts the loaded
source to the capture field's actual CLR type in both modes, retaining per-iteration snapshots.

The [live fixture](../../tests/fixtures/HoistedDebuggerAcceptance/main.ts) now uses `await delay()`
before its numeric loop. Its existing markers check all three iteration values `0/1/2`, their sum
`3`, and restored outer `i = 7`. The fresh VS Code C# run passes all 55 stops and 123 expected
Locals/Watch values, with identical debug and nondebug output. The original numeric control now
passes IL verification and prints `result 3 7` in both modes.

The fresh secondary run uses the same primary assembly. All 123 first Locals and Watch values,
29 scope-absence checks, and two hidden-field checks pass. Its seven duplicate-name Locals cases
match #1985 exactly, with correct first values and Watches; no empty-name entries or compiler
scaffolding appear. The saved secondary audit is
`artifacts/hoisted-numeric-timer/secondary/audit.json`.

The [numeric-timer verification record](../../scripts/editor-smoke/compiled-debugger/numeric-timer-last-verified.json)
identifies the follow-up's sources, binaries, and bounded checks. The earlier failing control
remains in `artifacts/hoisted-investigation/numeric-real`; #1985's accepted numeric fixture used
the valid `Promise.resolve` path. The original full-suite and conformance results above describe
#1985 and remain in its unchanged verification record.

## Remaining limits

The C# and netcoredbg expression evaluators have different presentation rules;
the accepted target does not require their Locals views to be identical. Types
and debugger infrastructure remain inspectable through raw CLR views, even when
they are hidden from source Locals.

The primary C# evaluator escapes C# keywords: source `int` displays and evaluates
as `@int`, while bare `int` fails with CS1525. The legal TypeScript name `$dollar`
was checked in both Locals and Watch and evaluates directly. This comparison is
recorded in `artifacts/name-probe/run-yk1TAA`.

An async arrow borrowing an ordinary synchronous closure's display class can
show additional plain-named fields from that same shared closure. Those fields
lack the numeric hoisted slots used to filter suspension-owner captures. Selected
capture values remain authoritative, but that ordinary-closure path does not
have the same independent field filtering as the tested suspension-owner path.

The parent-constant projection covers the immediate parent arrow. Arbitrary-depth
constant proxy relays and parent-owned mutable or parameter captures without an
authoritative shared display class are not covered by that projection. Such
captures can still require inspecting the raw parent frame; the verified shared
display-class capture paths retain source projection.

A deeper loop-capture relay (`() => () => i`) remains incorrect at runtime in
both builds: its expected sum is `3`, but unchanged `5256a80b` produces `0` and
this compiler produces `21`, with and without symbols. Both assemblies pass IL
verification. This unsupported lowering shape is outside the accepted direct
per-iteration capture path; its values are not claimed to be baseline-equivalent.
The exact controls are in `artifacts/hoisted-investigation/numeric/relay-results.json`.

Nondebug async and async-generator `for-of` loops retain an inherited readonly-capture
shadowing bug when `const item` shadows outer `item = 7`. Both the frozen #1985 compiler and
this follow-up print `result 21 7` for the async control instead of `result 3 7`; the
async-generator control prints `result 3 2` instead of `result 3 7`, then yields `3` and
completes normally. All four assemblies pass IL verification. The focused `for-of` parity
tests use a distinct loop binding; same-name numeric `for` loop regressions remain covered.
Controls and output are in `artifacts/hoisted-numeric-timer/shadow-for-of`.

Some preexisting capture lowering paths use a single name-keyed storage cell for
distinct shadows: captures through named functions/classes and writes from an
async arrow can prevent independent renaming. Such bindings do not have distinct
stable storage to project, so independent shadow lifetimes cannot be promised.

Two unrelated loop limitations were reproduced with an isolated build of
unchanged `5256a80b`, with and without symbols: an async or async-generator
`for-in` loop with two keys and a genuinely suspending await visits only the first
key; a first declaration in `for (var item of/in ...)` is rejected if `item` is
used after the loop. The runtime binding checks use a single-key `for-in` and a
prior `var` declaration, while retaining multi-iteration `for-of` checks. The
exact unchanged controls are in `artifacts/hoisted-investigation/baseline-runtime`.
