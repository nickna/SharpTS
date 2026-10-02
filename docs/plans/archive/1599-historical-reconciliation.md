# Frozen #1599 historical reconciliation

This is the finite H01 outcome for [#1926](https://github.com/nickna/SharpTS/issues/1926),
under [#1867](https://github.com/nickna/SharpTS/issues/1867). The historical declaration
and construction migrations remain credited. Their residual ownership obligations and
unrepaired behavior reports have specific destinations below. No inventory count certifies
ownership or JavaScript conformance, and no other issue is closed by this report.

## Evidence boundary and how to read the tables

The input is the preserved [#1599 body](https://github.com/nickna/SharpTS/issues/1599)
at the 2026-09-27T18:53:55Z freeze, plus the original eighteen comments ending at
[5858750427](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5858750427).
The body is 261,770 UTF-16 code units; its UTF-8 SHA-256 is
`420cdca4eaf29b19354bcce748ffa034f4cfaa3400e772fb2089b8d97978c15f`.
The subsequently updated issue timestamp does not change those body bytes.
The nineteenth comment, publication/transfer comment 5863574793, is excluded from
historical coverage; successor issue bodies establish destinations only.

Source and architecture checks use commit
[`83a41096108fe6de739fa7cfcb148c4bb1193121`](https://github.com/nickna/SharpTS/commit/83a41096108fe6de739fa7cfcb148c4bb1193121),
which was also the checkout HEAD during this pass. `L` references below are one-based
lines in the exact frozen body split on LF, not lines in a reformatted browser display.
The [coverage ledger](1599-historical-reconciliation.json) records each nonempty input
line's number and SHA-256, all eighteen comment hashes and URLs, and its report rows.
Repeated statements of the same obligation point to the same disposition. Statements
about old verification totals are credited to the reported head and are not new test results.

The dispositions have deliberately different meanings:

- **Verified repair:** the specific reported expectation has matching source/regression
  coverage, supplemented by this pass's focused checks where stated.
- **Retained contract:** source and the frozen architecture describe intentional authority,
  availability, identity or lifetime; the named ownership child still owns its final verification.
- **Transfer:** the original observation remains actionable in a specific existing child;
  a transfer is not a repair or a passing conformance case.
- **Missing evidence:** this pass could not recover the original artifact. The limitation and
  finite recovery/current-reproduction destination are explicit. No invented source or inferred
  runtime result substitutes for it.

## Named supersession candidates

These checks compare original expectations with implementation and executable regressions,
not PR titles. Paths below are relative to `tests/SharpTS.Tests/` unless prefixed with `src/`.
The regression classes and exact filters are recorded in the validation section and ledger.
Missing old output bundles limit historical hash/output comparison even when a committed
regression supports the specific repair.

| ID | Frozen observation/source | Specific evidence and supported disposition |
| --- | --- | --- |
| S01 | L101–109, L115, L807, L811, L835, L837: JSON present/minimal/present emission leaks the first assembly; `JsonStringify` cannot load it | [#1689](https://github.com/nickna/SharpTS/pull/1689): `EmittedJsonRuntimeTests.ReusedEmitterKeepsAllJsonHandlesAndGuestStoresInTheirOwnOutput` exercises both hosted and ordinary output, real parse/stringify, stores and assembly references. `src/SharpTS/Compilation/RuntimeEmitter.Json.cs` no longer retains the seventeen helpers. Verified specific repair; broader JSON ownership goes to R04. |
| S02 | L115, L839: descriptor/prototype negative assertion could be vacuous | [#1689](https://github.com/nickna/SharpTS/pull/1689): `JsonTypedScalarRecordTests.ClosedRecordArraySelectsExactWriterOutsideTheElementLoop` uses the actual `$PropertyDescriptorStore.HasPropertyDescriptors` / `HasPrototypeEntry` names and the same predicate on fallback IL as a positive control. Verified verification-gap correction. |
| S03 | L93, L813, L835: Boxed helpers read global BigInt/Date flags despite supplied metadata | [#1679](https://github.com/nickna/SharpTS/pull/1679): `EmittedBoxedPrimitiveRuntimeTests.WrapperCreationUsesExplicitBigIntPrototypeAvailability` and `DefaultConversionUsesExplicitDateAvailability` cover opposite selection inputs and execute emitted helpers. Verified dependency contract; boxed Number behavior is B16, not repaired here. |
| S04 | L147, L813, L837: TypedArray detection ignores supplied implementation when globally off, or reads absent implementation when on | [#1705](https://github.com/nickna/SharpTS/pull/1705): all four rows of `EmittedObjectReadRuntimeTests.TypedArrayDetectionFollowsImplementationDespiteGlobalSelection` inspect saved IL and invoke the generated detector. Verified contract; normal caller feature gates remain. |
| S05 | L153, L813, L837: Readline skips supplied required helpers | [#1708](https://github.com/nickna/SharpTS/pull/1708): `ScopedFeatureGateTests.ReadlineHelpersFollowRequiredMetadataRegardlessOfGlobalSelection` covers both flags and prompt behavior. Verified contract. |
| S06 | L153, L813, L837: AbortSignal.any deployment reason comes from coarse/hidden feature state | [#1708](https://github.com/nickna/SharpTS/pull/1708): `ScopedFeatureGateTests.AbortAnyDependencyFollowsExplicitSelection` covers independent coarse, fine and supplied selection. Verified explicit dependency/deployment contract, not a demonstrated normal guest-output defect. |
| S07 | L559, L565–568: duplicate DNS declarations, split default-order state and Promise captures | [#1821](https://github.com/nickna/SharpTS/pull/1821): one `EmitDnsModuleMethods` call precedes Promise helpers; `EmittedDnsRuntimeTests` checks single-assignment declarations, all sixteen wrappers, shared state, saved IL and reuse. Verified repair. The literal/options behavior is S15; `$Module_promises` remains B11. |
| S08 | L568, L574–580: duplicate `Fs_lstatSync_Wrapper` and overwritten value-import entry | [#1822](https://github.com/nickna/SharpTS/pull/1822): `EmittedFileSystemRuntimeTests.LstatValueImportUsesOneWrapperPerAssemblyAcrossEmitterReuse` covers hosted/ordinary output and the sole registered wrapper. Verified repair; unrelated module-name collisions remain B11. |
| S09 | L7, L9, L13: aliased CLR/user-class name collision and char conversion | [#1835](https://github.com/nickna/SharpTS/pull/1835): three `DotNetImportTests` regressions cover same-named guest class, cross-module CLR identity and string/numeric char indexers. Original collision hash `bdf052e9e37e3e31f6a8283669889eb04c01b99606f39b51fa9efa72886f4c77` is retained by the source record. Verified repair; cache lifetime stays F15. |
| S10 | L553, L719–723, L797: cancel leaves an earlier read pending instead of `true/true` | [#1838](https://github.com/nickna/SharpTS/pull/1838): `StreamsWebSemanticTests.ReadableStream_CancelSettlesEarlierPendingRead` preserves the expected two true lines; source-result fulfillment/rejection, omitted reason and synchronous throws have separate regressions. Verified repair of the original hash `55383eae291599f02a3a69c01884ed42f15f5cddf0c9b90cb029cddbe0be84a2`. |
| S11 | L545, L727–731, L799: non-CA self issuer accepted; same-subject CA with different key rejected | [#1839](https://github.com/nickna/SharpTS/pull/1839): `CryptoX509IssuedTests.CheckIssuedUsesIssuerMetadataRatherThanSignatureVerification` asserts both directions and keeps `verify` separate. Authority identifiers/permissions and malformed extension controls cover the diagnostic cases. Verified specific correction; L799's older required wording is superseded. |
| S12 | L531–533, L735–739: missing method becomes `undefined`; Headers input loses `x-tag`; clone shares headers | [#1840](https://github.com/nickna/SharpTS/pull/1840): `FetchConstructionSemanticsTests` tests missing/undefined method defaults and independent Headers/Request/Response/fetched clones in interpreter, compiled and isolated standalone paths. Verified corrections. Header immutability and argument conversion are S13/S14. |
| S13 | L743–747, L841–843: fetched Headers mutability and unstable getter/clone identity | [#1841](https://github.com/nickna/SharpTS/pull/1841): `FetchHeaderGuardTests` checks stable original headers, distinct immutable clone headers, all three mutations including absent deletion, and mutable explicit copies. Verified correction. |
| S14 | L747, L751–755, L843: Headers arguments bypass guest conversion/order | [#1842](https://github.com/nickna/SharpTS/pull/1842): `HeadersArgumentConversionTests` checks name-before-value/guard conversion, toString/valueOf, Symbols, nonprimitive results and abrupt completion without writes in three modes. Original source hash `864092a9ad3753812648435fdc4048a746de41669554ad7c5ee8d1bd343a167a` remains in the frozen record. Verified correction; L843's older required wording is superseded. |
| S15 | L392, L568, L759–763: literal IPv4 with family option returns wrong address/family | [#1843](https://github.com/nickna/SharpTS/pull/1843): `DnsLookupOptionsTests` preserves literals, numeric/object family, all results and stable ordering/default precedence; the standalone variant uses the same program. Original hash `998a505899485911dfd14e19f3d7e4f04b5abb2aca50c26926b4bc99610335c7`; the recorded Linux baseline already passing is credited separately. Verified Windows discrepancy repair; not all hints/invalid-input conformance. |
| S16 | L395, L767–769: generic Promise.withResolvers rejected even with explicit ES2024 | [#1844](https://github.com/nickna/SharpTS/pull/1844): `Modules/LibraryInterfaceAugmentationTests` tests ES2024/ES2025, ambient augmentations and scope/negative controls. `TypeChecker` refreshes interface value identities per check. Verified specific library augmentation correction; unchanged default ES5 selection is not a failure of that repair. |
| S17 | L305, L319, L329, L343, L773–777: early Run/WaitForTask omit cancellation against #74 | [#1845](https://github.com/nickna/SharpTS/pull/1845): `EmittedCancellationRuntimeTests.EventLoopCancellationStopsCallbackDrain` covers Run/WaitForTask/PumpOnce in hosted/ordinary output; `DistantTimerDoesNotDelayCancellation` covers bounded waits. Source declares CheckCancellation before the drains and preserves the public flag/exception. Verified repair of that omission. |
| S18 | L467–501, L781–785: completed faults precede `scheduled`; pending capture prints true/false | [#1846](https://github.com/nickna/SharpTS/pull/1846): `EventsRejectionMonitorTests.CompletedRejectionRunsAfterEmitReturns` and `PendingListenerRejectionIsCaptured` retain those original programs/expectations. Checkpoint, timer and restoration controls cover routing. Verified specific repair. Later overdue-timer interaction is S20. |
| S19 | L388–390, L789: named `new TLSSocket` rejected; unconnected/destroyed protocol and encrypted identity diverge | [#1847](https://github.com/nickna/SharpTS/pull/1847): the first three `TlsModuleTests` exercise named/prefixed/aliased TLSSocket, exact secure-context constructor, negative cipher-query control and pre/post-destroy state. Verified reported constructor/lifecycle correction; no handshake/platform-wide conformance claim. |
| S20 | L95, L211, L465, L517, L667, L789, L793: recurring generator deadline; workers remain parked; overdue timers omit checkpoints | [#1848](https://github.com/nickna/SharpTS/pull/1848): `GeneratorLifetimeTests` and `TimerMicrotaskCheckpointTests` verify shutdown/disposal and each timer checkpoint. The PR records three default-spin failures and zero-spin success under the original 100,000-yield/30-second contract, plus unloaded latency tradeoffs. Verified shutdown/checkpoint changes and recorded contention improvement; the historical allocation and arbitrary-load diagnosis stays B14. |
| S21 | L317 and C01: #1805 loses async local C before/after await | [#1849](https://github.com/nickna/SharpTS/pull/1849): the three exact issue sources match `AsyncLocalClassDeclarationTests.DeclarationAfterIntrinsicAwait_ResolvesLocalClass`, `DeclarationAfterIntrinsicAwait_DoesNotReject`, and `DeclarationBeforeIntrinsicAwait_ResolvesLocalClass`. New `StandaloneDllTests.Isolated_Issue1805OriginalPrograms_ResolveLocalClassBindings` retains the exact programs, stdout `5\n`, empty stderr, 30-second execution limit, saved IL verification and absence of SharpTS dependency. See the dedicated decision below. |
| S22 | C08–C11: generic private instance members emit invalid open owners / TypeLoadException | [#1854](https://github.com/nickna/SharpTS/pull/1854): `GenericPrivateOwnerTests` exercises checked declaration-owned interfaces, private brand identity, ordinary/closure/async/generator and suspending/rest arguments. Verified covered instance-owner repair; method values, inherited interpreter dispatch, static-private checking and private-in remain B02–B05. |
| S23 | C12–C13: module/CLI generic expression constructs open CLR Type | [#1856](https://github.com/nickna/SharpTS/pull/1856): `ModuleGenericClassExpressionTests` uses module compilation with saved IL, runtime constructor bindings, aliases/imports/shadowing, argument order and void/never erasure. Verified construction correction; compatibility is separately S24/S25/B06. |
| S24 | C13–C14: same generic constructor rejects its own typeof alias/reassignment | [#1857](https://github.com/nickna/SharpTS/pull/1857): identity/reflexivity tests and `ModuleGenericClassExpressionTests.RuntimeBindingsAndArgumentsArePreserved` retain the reassignment without suppressed diagnostics. Verified same-declaration correction; distinct-declaration structural compatibility remains B06. |
| S25 | C14–C15: forward signature invents ClassExpr_1/ClassExpr_2; compatibility cache conflates identities | [#1858](https://github.com/nickna/SharpTS/pull/1858): `GenericConstructorIdentityTests` and forward-alias regressions preserve expression declaration identity and reject distinct same-named private/cache cases while still checking bodies. Verified covered identity repair; emitted owners and namespace cases are separate S26/B09. |
| S26 | C10–C16: same-named class-expression emitted owner/parent/dispatch collision | [#1859](https://github.com/nickna/SharpTS/pull/1859): `ClassExpressionOwnerIdentityTests` covers distinct names/owners, parent constructors, nonvirtual super, modules and both import orders. Verified covered expression collisions; absent original namespace sources prevent declaring all B09 cases duplicates. |
| S27 | C16–C17: unique generic expression static method throws ContainsGenericParameters | [#1860](https://github.com/nickna/SharpTS/pull/1860): `GenericClassStaticValueTests` and `GenericStaticMethodModuleTests` execute direct/inherited/extracted methods, state, descriptors and independent owners. Verified covered method-value repair. |
| S28 | C17–C18: generic static field reads open owner; alias writes diverge; checker access/readonly rules | [#1861](https://github.com/nickna/SharpTS/pull/1861): `GenericClassStaticFieldValueTests`, `GenericStaticFieldModuleTests` and `GenericStaticFieldDescriptorTests` cover storage, inherited shadows, identity, static types and readonly/private/protected checks. Verified covered field/access correction; interpreter non-writable descriptor assignment remains B01. |

### #1805 acceptance decision

The earlier #1806 initializer ownership change explicitly preserved all three failures;
it is not the fixing phase. #1849 supplies the binding/definition-site repair and the
committed dual-mode tests contain all three original #1805 programs. The new standalone
theory makes those exact acceptance sources permanent CLI regressions rather than inferring
closure from the more complex computed-key programs.

The focused validation includes all existing `AsyncLocalClassDeclarationTests` controls
(ordinary/async/generator bindings, declaration/expression, real suspension, shadowing and
computed definition work), plus `EmittedClassInitializationRuntimeTests` for saved IL,
hosted/ordinary metadata reuse and initializer caching/exception identity. Hosted coverage
here is those existing lifecycle/compilation checks, not a claim that every original top-level
program was executed through a hosted factory. The historical original Node result is `5\n`;
no fresh Linux or Node rerun is implied by this Windows pass.

#1805's three original standalone failures have an
evidence-backed prior-repair disposition against #1849. This report recommends that specific
resolution; it does not close #1805. Repeated evaluations sharing CLR identity/computed-key
state and runtime-valued dynamic heritage remain [#1906](https://github.com/nickna/SharpTS/issues/1906)
and [#1907](https://github.com/nickna/SharpTS/issues/1907).

## Credited runtime migrations and remaining ownership destinations

The following is a source-to-destination table for **verification obligations**, not a new
defect list. It credits the completed phases in L15–197 and L199–793, including the later
construction and checked registry work. The children already partition all 126 roots;
their fixed symbol lists govern the remaining producer/consumer/local/alias checks.

| ID | Credited families/phases and frozen source | Finite ownership destination |
| --- | --- | --- |
| R01 | Function attributes/construction/values/binding/prototypes/introspection/reflected methods/invocation/construction/call arguments: L155–171; static dispatch L283–295 | [#1868](https://github.com/nickna/SharpTS/issues/1868); shared per-arity argument buffers are retained implementation authority, with the demonstrated nested overwrite separately #1727. |
| R02 | Generator protocols, stable results, async adapters, captured records, wrappers, protocol, materialization/helpers and disposal: L173–185, L233–241 | [#1869](https://github.com/nickna/SharpTS/issues/1869); captured-next/return protocols and optional adapters remain distinct owners. |
| R03 | Object storage/descriptors/Reflect, accessors, state, prototypes/keys/predicates/operations/construction/delete/read/write/operators/object-fields/receiver guards: L105–109, L123, L129–151, L197, L253–261 | [#1870](https://github.com/nickna/SharpTS/issues/1870); mutable guest weak tables and descriptor/prototype storage are separate from frozen compiler metadata. |
| R04 | JSON, record/scalar storage and late Program shape fields: L115–117, L195; regex literal cache L321–329; promoted shapes/union lifecycle L673–699 | [#1871](https://github.com/nickna/SharpTS/issues/1871); S01/S02 repairs stay credited; late legitimate registration/finalization and conservative fallback are retained. Mapper/configuration audit also F14. |
| R05 | Module loading/VM/source execution L59–63, global object L207–213, namespaces L215–221, subscriptions L223–231, shared runtime type L335–343, export/deployment owners L583–620 | [#1872](https://github.com/nickna/SharpTS/issues/1872); by-name soft dependencies and public hosted/cancellation ABI are retained; separate user module captures are F09/F12. |
| R06 | Strings/templates/coercion/wrappers/Number/Math/BigInt/Boolean/numeric conversion L87–103; Date/RegExp/errors L125–131; URI/sentinels L191–205; string symbol dispatch L263–271 | [#1873](https://github.com/nickna/SharpTS/issues/1873); historical behavior differences stay in specific B or existing semantic rows. |
| R07 | Array storage/operations L22–23, construction L503–521; Map/Set/weak collections/ref/finalization L119–121 | [#1874](https://github.com/nickna/SharpTS/issues/1874); guest queues/finalizers stay mutable. Deterministic finalizer body coverage does not prove GC scheduling. |
| R08 | Promise L21 and L346–352, timers/event loop L35–36, Abort/ALS L65–67, cancellation L297–307, S17/S18/S20 | [#1875](https://github.com/nickna/SharpTS/issues/1875); pending tasks and cross-assembly job isolation remain verification targets. |
| R09 | Process/child process/OS/console/inspection/text/readline/Intl/performance/TTY/errors/cluster L45–77 | [#1876](https://github.com/nickna/SharpTS/issues/1876); existing lazy clocks, terminal coercion, error markers and deployment remain credited. Console first-invocation failure is B13. |
| R10 | Filesystem sync/async/streams/watchers L39–43 and S08 | [#1877](https://github.com/nickna/SharpTS/issues/1877); one wrapper does not complete watcher, async registry or local-construction ownership verification. |
| R11 | DNS/Net/TLS/UDP/HTTP/fetch L16/L18–20/L26–27; construction L354–457/L523–539; S07/S12–S15/S19 | [#1878](https://github.com/nickna/SharpTS/issues/1878); early factories, deferred workers and client/cache isolation remain checked boundaries. |
| R12 | Buffer/ArrayBuffer/SharedArrayBuffer/DataView/TypedArray/Atomics L28–33/L75 | [#1879](https://github.com/nickna/SharpTS/issues/1879); S04 does not certify complete storage, adapters or shared-memory ownership. |
| R13 | Crypto/WebCrypto L24–25 and construction L541–547; S11 | [#1880](https://github.com/nickna/SharpTS/issues/1880); algorithm catalogs and local construction are credited, not proof of all crypto behavior. |
| R14 | EventEmitter/Zlib/Node/Web streams L17/L34/L37–38, construction L449–501/L549–555 and S10/S18 | [#1881](https://github.com/nickna/SharpTS/issues/1881); BCL collection handles are not evidence of a generated-assembly cache leak. |
| R15 | MessageChannel/Port/Worker/Broadcast/structured clone L79–85 | [#1882](https://github.com/nickna/SharpTS/issues/1882); cross-thread references, queued delivery, clone isolation, receive caches and collectible realm lifetime need their own verification. |

## Compiler-state, shared and local contract destinations

These destinations account for recurring “all local builders/shared state/remaining inventory”
paragraphs and the frozen architecture/source inventory; they do not assume that a mutable map
is defective. The completed #1850–1855 class owners are credited alongside the older external,
union, shape, framework and registry phases.

| ID | Named boundary and retained/unfinished obligation | Destination |
| --- | --- | --- |
| F01 | Class declaration, constructor, parent, deferred initialization/key identity; credit #1850 and S21–S28 | [#1883](https://github.com/nickna/SharpTS/issues/1883) |
| F02 | Public members, computed-member/property-dispatch/private/generic class owners; credit #1851–1855, canonical TypeBuilder keys and completion | [#1884](https://github.com/nickna/SharpTS/issues/1884) |
| F03 | Typed CLR property handles and immediate aliases | [#1885](https://github.com/nickna/SharpTS/issues/1885) |
| F04 | Lock-decorator generated fields and their declaration lifetime | [#1886](https://github.com/nickna/SharpTS/issues/1886) |
| F05 | Generic function parameters; checked declaration owner is unfinished, not waived by class-generic #1855 | [#1887](https://github.com/nickna/SharpTS/issues/1887) |
| F06 | Ordinary function/overload/specialized-call state | [#1888](https://github.com/nickna/SharpTS/issues/1888) |
| F07 | Arrow display-class state | [#1889](https://github.com/nickna/SharpTS/issues/1889) |
| F08 | Function/nested closures and capture identities | [#1890](https://github.com/nickna/SharpTS/issues/1890) |
| F09 | Entry-point/module captures | [#1891](https://github.com/nickna/SharpTS/issues/1891) |
| F10 | Async state-machine construction/captures; repairing local C does not finish metadata ownership | [#1892](https://github.com/nickna/SharpTS/issues/1892) |
| F11 | Generator/async-generator state-machine construction/captures | [#1893](https://github.com/nickna/SharpTS/issues/1893) |
| F12 | User-module declarations/exports/initializers and naming | [#1894](https://github.com/nickna/SharpTS/issues/1894) |
| F13 | Enum caller registries/literal metadata, distinct from completed runtime reverse-lookup owner | [#1895](https://github.com/nickna/SharpTS/issues/1895) |
| F14 | Mapper/configuration/feature selection and generated representation stores; local builders can have legitimate late writes | [#1896](https://github.com/nickna/SharpTS/issues/1896) |
| F15 | TypeProvider keys/caches; external identity; DotNetSynthesisCache and DotNetTypeRegistry generations/returned arrays. Credit #1834/#1836/#1837; weak keys, generation replacement and retained old metadata are intentional. Returned method/indexer arrays remain shared and read-only by contract | [#1897](https://github.com/nickna/SharpTS/issues/1897) |
| F16 | Credit #1824–1831: immutable framework/value/export/Date catalogs; compiler-owned completed type/module strategy tables; shared completed eighteen-stateless-handler chain. Catalog selectors resolve supplied owners and must not retain generated handles | [#1898](https://github.com/nickna/SharpTS/issues/1898) |
| F17 | RuntimeTypes/reflection bridges, DNS best-effort synchronization and mutable target lifetimes. Pure BCL and graceful optional fallbacks are not soft requirements; real required managed bridges record capabilities | [#1899](https://github.com/nickna/SharpTS/issues/1899) |

The [frozen architecture](https://github.com/nickna/SharpTS/blob/83a41096108fe6de739fa7cfcb148c4bb1193121/ARCHITECTURE.md)
supports these retained contracts: explicit optional availability; checked forward declarations;
repair by supplying omitted declarations rather than replacing published identity; completion
after legitimate body/finalization work; immutable metadata views; mutable isolated guest state;
per-emission construction values; and separate shared framework/weak-cache authority. L818–828's
feature-present/absent, optimization, deployment and IL obligations follow the child that owns the
named boundary. L830–837's old blanket loop/closure instructions are historical; their remaining
work has these finite destinations. No complete audit row is certified by flat-zero, field counts,
source binding without errors, a file split, or this reconciliation.

## Specific recent and older historical transfers

All 26 original observation groups have destinations. B01–B11 are the recent behavior children;
B12–B26 are older historical children. Each retains the original expectation, with recovery
or current reproduction where the source is unavailable. S22–S28 supersede only their covered
cases, not neighboring findings.

| ID | Source / observation that remains transferred | Destination |
| --- | --- | --- |
| B01 | C18: interpreter ignores a non-writable static descriptor | [#1900](https://github.com/nickna/SharpTS/issues/1900) |
| B02 | C09–C11/C13: private-method values rejected in generic and nongeneric classes | [#1901](https://github.com/nickna/SharpTS/issues/1901) |
| B03 | C08–C11: inherited private-method interpreter dispatch | [#1902](https://github.com/nickna/SharpTS/issues/1902) |
| B04 | C08–C09: private-in parser syntax unsupported; recover the original source/diagnostic before declaring parity | [#1903](https://github.com/nickna/SharpTS/issues/1903) |
| B05 | C08–C11: generic static-private lookup/type checking, not instance-owner repair | [#1904](https://github.com/nickna/SharpTS/issues/1904) |
| B06 | C14–C15: distinct generic constructor structural compatibility, distinct from same-declaration identity | [#1905](https://github.com/nickna/SharpTS/issues/1905) |
| B07 | C01–C05: repeated class evaluations share CLR identity and overwrite computed-key state | [#1906](https://github.com/nickna/SharpTS/issues/1906) |
| B08 | C01–C05: awaited runtime-valued dynamic superclass | [#1907](https://github.com/nickna/SharpTS/issues/1907) |
| B09 | C10–C11/C15: namespace/module duplicate property-dispatch declarations; S26 proves the expression cases only | [#1908](https://github.com/nickna/SharpTS/issues/1908) |
| B10 | L513: CLI rejects extends Array<number> and extends Array; exact typed single-file API program passes | [#1909](https://github.com/nickna/SharpTS/issues/1909) |
| B11 | L568/L580: unrelated duplicate `$Module_promises`; recover exact imports/order before attributing to sanitized names | [#1910](https://github.com/nickna/SharpTS/issues/1910) |
| B12 | L339/L343: two retained grouping observations / aliased Map.groupBy discrepancy, publication previously rejected | [#1911](https://github.com/nickna/SharpTS/issues/1911): [bounded reconciliation](issue-1911-reconciliation.md) records missing original per-case evidence and a current constructor-alias runtime failure transferred to [#1928](https://github.com/nickna/SharpTS/issues/1928). Old failures remain preserved; no original-case repair is claimed. |
| B13 | L155/L509–511: first harness call restores a raw ProxyWriter through Console's SyncTextWriter and fails identity; second/full-suite invocation passes | [#1912](https://github.com/nickna/SharpTS/issues/1912); diagnosed, not repaired by the Array migration. |
| B14 | L95/L211/L465/L517/L667/L789: generator timeout and 7,648-byte string-search allocation against 1,024-byte limit; passing retries do not establish cause | [#1913](https://github.com/nickna/SharpTS/issues/1913); credit S20's measured spin/lifetime change but retain allocation and other timing records. |
| B15 | L89: original CLI declaration rejects String.raw, so phase probes used --noLib; computed-tag receiver behavior preserved | [#1914](https://github.com/nickna/SharpTS/issues/1914) |
| B16 | L93/L103: boxed/explicit Number exotic string hooks and async Number lookup; wrapper-before-async probe avoids the latter | [#1915](https://github.com/nickna/SharpTS/issues/1915) |
| B17 | L95: explicit radix-16 prefix parsing mismatch; reviewer withdrawal only recognized baseline provenance | [#1916](https://github.com/nickna/SharpTS/issues/1916) |
| B18 | L97: Math identity, aliased nonfinite sum and signed-zero adapters; absent early sumPrecise target is retained staging behavior | [#1917](https://github.com/nickna/SharpTS/issues/1917) |
| B19 | L99: BigInt async static dispatch, generator literal, typed-array assignment and CommonJS alias limitations | [#1918](https://github.com/nickna/SharpTS/issues/1918) |
| B20 | L67/L101/L103: primitive constructor CommonJS/resolver aliases rejected before emission | [#1919](https://github.com/nickna/SharpTS/issues/1919) |
| B21 | L105: inherited accessor and top-level strict compatibility | [#1920](https://github.com/nickna/SharpTS/issues/1920) |
| B22 | L127: RegExp species/custom-exec exploratory differences; original Node expectations retained | [#1921](https://github.com/nickna/SharpTS/issues/1921) |
| B23 | L131: callback DelegateCtor IL-verifier finding with unchanged IL and expected execution; runtime success does not waive invalid IL | [#1922](https://github.com/nickna/SharpTS/issues/1922) |
| B24 | L135/L143: custom Symbol tag and combined Symbol getter/setter observation | [#1923](https://github.com/nickna/SharpTS/issues/1923) |
| B25 | L137: compiled symbol delete/reinsert order; interpreter-only expectation is not a compiled passing case | [#1924](https://github.com/nickna/SharpTS/issues/1924) |
| B26 | L139: array/string length and Promise callback name enumerability | [#1925](https://github.com/nickna/SharpTS/issues/1925) |

### Existing semantic reports

The following generated table retains each of the 63 explicitly published semantic reports.
Every row cites the frozen paragraph that explains its expectation. They remain transfers,
apart from S21's verified #1805 prior repair; no result is inferred from their open/closed state.

| ID | Frozen source and original expectation | Destination / disposition |
| --- | --- | --- |
| E1713 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L435: Compiled Array.from rejects bound and built-in callable mappers | [#1713](https://github.com/nickna/SharpTS/issues/1713); Transfer; original failure/control expectations retained. |
| E1714 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L433: Compiled nested call/apply wrappers return null instead of invoking the target | [#1714](https://github.com/nickna/SharpTS/issues/1714); Transfer; original failure/control expectations retained. |
| E1715 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L433: Saved compiled Proxy call/apply/bind programs exceed the execution deadline | [#1715](https://github.com/nickna/SharpTS/issues/1715); Transfer; original failure/control expectations retained. |
| E1717 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L431: Compiled twice-bound functions lose their name | [#1717](https://github.com/nickna/SharpTS/issues/1717); Transfer; original failure/control expectations retained. |
| E1718 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L431: Typed Reflect.construct rejects a class constructor accepted through any | [#1718](https://github.com/nickna/SharpTS/issues/1718); Transfer; original failure/control expectations retained. |
| E1721 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L429: Extracted TypedArray fill called through .call fails as non-callable | [#1721](https://github.com/nickna/SharpTS/issues/1721); Transfer; original failure/control expectations retained. |
| E1722 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L429: Extracted TextDecoder.decode through .call returns empty text | [#1722](https://github.com/nickna/SharpTS/issues/1722); Transfer; original failure/control expectations retained. |
| E1724 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L427: Dynamic new on a bound function mutates its bound receiver | [#1724](https://github.com/nickna/SharpTS/issues/1724); Transfer; original failure/control expectations retained. |
| E1725 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L427: Dynamic new accepts arrow async and generator functions | [#1725](https://github.com/nickna/SharpTS/issues/1725); Transfer; original failure/control expectations retained. |
| E1727 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L425: Nested dynamic method calls overwrite an outer pooled argument | [#1727](https://github.com/nickna/SharpTS/issues/1727); Transfer; original failure/control expectations retained. |
| E1729 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L423: Compiled dynamic generators do not expose Symbol.iterator | [#1729](https://github.com/nickna/SharpTS/issues/1729); Transfer; original failure/control expectations retained. |
| E1730 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L423: Compiled yield-star string delegation splits surrogate pairs | [#1730](https://github.com/nickna/SharpTS/issues/1730); Transfer; original failure/control expectations retained. |
| E1732 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L421: Compiled async generator throw rejects instead of resuming its catch | [#1732](https://github.com/nickna/SharpTS/issues/1732); Transfer; original failure/control expectations retained. |
| E1733 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L421: Compiled async yield-star loses the delegated return value | [#1733](https://github.com/nickna/SharpTS/issues/1733); Transfer; original failure/control expectations retained. |
| E1734 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L421: Compiled async iterator methods do not preserve captured counter updates | [#1734](https://github.com/nickna/SharpTS/issues/1734); Transfer; original failure/control expectations retained. |
| E1735 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L421: For-await element inference retains Promise types instead of awaited values | [#1735](https://github.com/nickna/SharpTS/issues/1735); Transfer; original failure/control expectations retained. |
| E1739 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L419: Reject BigInt and Symbol results from compiled IteratorClose | [#1739](https://github.com/nickna/SharpTS/issues/1739); Transfer; original failure/control expectations retained. |
| E1740 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L419: Resolve node:stream Readable static members in standalone output | [#1740](https://github.com/nickna/SharpTS/issues/1740); Transfer; original failure/control expectations retained. |
| E1742 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L417: Honor own indexed getters during compiled array and argument spread | [#1742](https://github.com/nickna/SharpTS/issues/1742); Transfer; original failure/control expectations retained. |
| E1743 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L417: Preserve standalone Unicode output under Windows CreateNoWindow | [#1743](https://github.com/nickna/SharpTS/issues/1743); Transfer; original failure/control expectations retained. |
| E1745 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L415: Return undefined from compiled iterator forEach | [#1745](https://github.com/nickna/SharpTS/issues/1745); Transfer; original failure/control expectations retained. |
| E1746 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L415: Reject negative limits in compiled iterator take | [#1746](https://github.com/nickna/SharpTS/issues/1746); Transfer; original failure/control expectations retained. |
| E1747 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L415: Close the source when compiled iterator take reaches its limit | [#1747](https://github.com/nickna/SharpTS/issues/1747); Transfer; original failure/control expectations retained. |
| E1748 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L415: Wrap iterable arrays correctly in compiled Iterator.from | [#1748](https://github.com/nickna/SharpTS/issues/1748); Transfer; original failure/control expectations retained. |
| E1750 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L413: Preserve iterator consumption and closing during array destructuring | [#1750](https://github.com/nickna/SharpTS/issues/1750); Transfer; original failure/control expectations retained. |
| E1751 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L413: Reject non-iterable destructuring sources with guest TypeError | [#1751](https://github.com/nickna/SharpTS/issues/1751); Transfer; original failure/control expectations retained. |
| E1752 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L413: Investigate Symbol.iterator assignment rejection on number[] arrays | [#1752](https://github.com/nickna/SharpTS/issues/1752); Transfer; original failure/control expectations retained. |
| E1753 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L413: Honor array iterator overrides when destructuring an any-typed source | [#1753](https://github.com/nickna/SharpTS/issues/1753); Transfer; original failure/control expectations retained. |
| E1754 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L413: Initialize CLR enumerable inputs before consuming iterator state | [#1754](https://github.com/nickna/SharpTS/issues/1754); Transfer; original failure/control expectations retained. |
| E1756 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L411: Type checker rejects a super method inherited through an intermediate class | [#1756](https://github.com/nickna/SharpTS/issues/1756); Transfer; original failure/control expectations retained. |
| E1757 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L411: Direct super calls fail IL verification in async and generator state machines | [#1757](https://github.com/nickna/SharpTS/issues/1757); Transfer; original failure/control expectations retained. |
| E1758 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L411: Synchronous arrows fail to retain the receiver for super method values | [#1758](https://github.com/nickna/SharpTS/issues/1758); Transfer; original failure/control expectations retained. |
| E1759 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L411: Async arrow super method values reject instead of invoking the parent method | [#1759](https://github.com/nickna/SharpTS/issues/1759); Transfer; original failure/control expectations retained. |
| E1761 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L409: Compiled assignment to a captured let binding before declaration omits ReferenceError | [#1761](https://github.com/nickna/SharpTS/issues/1761); Transfer; original failure/control expectations retained. |
| E1765 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L407: Compiled class own-key enumeration places added properties before earlier declared fields | [#1765](https://github.com/nickna/SharpTS/issues/1765); Transfer; original failure/control expectations retained. |
| E1767 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L405: Compiled URI component functions over-escape punctuation and accept malformed URI input | [#1767](https://github.com/nickna/SharpTS/issues/1767); Transfer; original failure/control expectations retained. |
| E1769 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L403: Compiled globalThis assignment ignores an installed accessor setter | [#1769](https://github.com/nickna/SharpTS/issues/1769); Transfer; original failure/control expectations retained. |
| E1770 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L403: Compiled global isNaN function values omit numeric coercion | [#1770](https://github.com/nickna/SharpTS/issues/1770); Transfer; original failure/control expectations retained. |
| E1771 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L403: Indirect dynamic eval omits the optional runtime deployment | [#1771](https://github.com/nickna/SharpTS/issues/1771); Transfer; original failure/control expectations retained. |
| E1772 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L403: Investigate Windows compiled processes exceeding the deadline after unhandled errors | [#1772](https://github.com/nickna/SharpTS/issues/1772); Transfer; original failure/control expectations retained. |
| E1774 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: CLI namespace function values are not callable | [#1774](https://github.com/nickna/SharpTS/issues/1774); Transfer; original failure/control expectations retained. |
| E1775 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: CLI namespace initializers cannot resolve private functions | [#1775](https://github.com/nickna/SharpTS/issues/1775); Transfer; original failure/control expectations retained. |
| E1776 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: CLI imported namespace member call fails with a null receiver | [#1776](https://github.com/nickna/SharpTS/issues/1776); Transfer; original failure/control expectations retained. |
| E1777 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: Compiled namespace property assignments are not reflected in subsequent reads | [#1777](https://github.com/nickna/SharpTS/issues/1777); Transfer; original failure/control expectations retained. |
| E1778 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: Compiled namespace missing members return null instead of undefined | [#1778](https://github.com/nickna/SharpTS/issues/1778); Transfer; original failure/control expectations retained. |
| E1779 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: Namespace deletion and subsequent member read disagree with JavaScript | [#1779](https://github.com/nickna/SharpTS/issues/1779); Transfer; original failure/control expectations retained. |
| E1780 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: Aliased namespace enums omit numeric reverse mappings | [#1780](https://github.com/nickna/SharpTS/issues/1780); Transfer; original failure/control expectations retained. |
| E1781 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L401: Namespace class-value probe emits unverifiable IL | [#1781](https://github.com/nickna/SharpTS/issues/1781); Transfer; original failure/control expectations retained. |
| E1784 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L399: Compiled using declarations look up the dispose method at scope exit | [#1784](https://github.com/nickna/SharpTS/issues/1784); Transfer; original failure/control expectations retained. |
| E1785 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L399: Compiled generator using declarations skip cleanup on return and completion | [#1785](https://github.com/nickna/SharpTS/issues/1785); Transfer; original failure/control expectations retained. |
| E1786 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L399: Break and continue from using scopes emit invalid IL in ordinary loops | [#1786](https://github.com/nickna/SharpTS/issues/1786); Transfer; original failure/control expectations retained. |
| E1788 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L397: Compiled enum reverse lookup fails for a negative member | [#1788](https://github.com/nickna/SharpTS/issues/1788); Transfer; original failure/control expectations retained. |
| E1789 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L397: Compiled enum reverse lookup throws instead of returning undefined for a missing key | [#1789](https://github.com/nickna/SharpTS/issues/1789); Transfer; original failure/control expectations retained. |
| E1790 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L397: Compiled enum value bindings are unavailable in aliases, computed members and generators | [#1790](https://github.com/nickna/SharpTS/issues/1790); Transfer; original failure/control expectations retained. |
| E1791 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L397: Compiled numeric enum reverse lookup emits invalid IL for typed values | [#1791](https://github.com/nickna/SharpTS/issues/1791); Transfer; original failure/control expectations retained. |
| E1794 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L269: Compiled String.match uses a RegExp intrinsic despite an own null Symbol.match | [#1794](https://github.com/nickna/SharpTS/issues/1794); Transfer; original failure/control expectations retained. |
| E1795 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L269: Compiled string symbol dispatch skips Number.prototype hooks on primitive arguments | [#1795](https://github.com/nickna/SharpTS/issues/1795); Transfer; original failure/control expectations retained. |
| E1796 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L269: Compiled borrowed String.match coerces the receiver before calling a custom symbol hook | [#1796](https://github.com/nickna/SharpTS/issues/1796); Transfer; original failure/control expectations retained. |
| E1798 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L279: TypeScript-accepted Reflect.apply numeric assertion is rejected by SharpTS | [#1798](https://github.com/nickna/SharpTS/issues/1798); Transfer; original failure/control expectations retained. |
| E1799 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L279: Saved Proxy constructor programs report not-a-constructor and exceed the execution deadline | [#1799](https://github.com/nickna/SharpTS/issues/1799); Transfer; original failure/control expectations retained. |
| E1801 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L291: Number predicate method-value program is rejected despite valid direct calls | [#1801](https://github.com/nickna/SharpTS/issues/1801); Transfer; original failure/control expectations retained. |
| E1802 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L291: Array.isArray property descriptors return a different function wrapper | [#1802](https://github.com/nickna/SharpTS/issues/1802); Transfer; original failure/control expectations retained. |
| E1805 | [#1599](https://github.com/nickna/SharpTS/issues/1599) L317: Compiled async functions cannot resolve local class declarations | [#1805](https://github.com/nickna/SharpTS/issues/1805); S21: verified prior repair; recommended resolution only. |

## Less visible limitations and failed preparation/probes

These dispositions prevent setup failures, controls and review history from disappearing behind
a concise phase summary. Closed preparation outcomes do not claim a guest behavior repair.

| ID | Frozen source and observation | Finite disposition / destination |
| --- | --- | --- |
| A01 | L81/L85: Worker full-feature expectations narrowed to actual deployment reasons; foreign BCL Task clone expectation corrected, actual emitted Promise rejection separately verified | Completed expectation corrections; R15 retains worker/cross-context/deployment ownership. No GC-scheduling or foreign Task conformance inferred. |
| A02 | L83/L85/L87/L89/L91/L93: external review lacked clone-backed analysis | Retain reported coverage and supplemental immutable-head local review separately; missing clone analysis is not a passed check. Recovery limit M03. |
| A03 | L95: Windows child-process E_HANDLE cleanup race | Recorded narrow cleanup repair with baseline-failing invalid-handle regression and fork controls; R09 retains process ownership. Not a generator fix. |
| A04 | L95/L97/L101/L115/L133/L187/L193: aggregate CI budgets, standalone partitioning and superseded/cancelled oversized attempts | Credit only completed exact-head gates and complete disjoint case coverage. Retain earlier timeout/cancelled/skipped attempts; no relaxed individual deadline, assertion or perf budget inferred. B14 owns original timing recurrence. |
| A05 | L97: delayed-rejection lifecycle fixture assumed reporting preceded fixed timers | Completed event-driven fixture correction with strict expected output/deadline retained; R08/S18/S20 are the actual scheduling boundaries. |
| A06 | L123/L131/L147/L149/L159/L195/L211/L287/L301: stale reflection adapter paths and strict Symbol write allocation correction | Recorded fixture corrections preserve assertions; #1693 separately repaired rejected strict writes. R03/R05 retain ownership; L147's Worker comment was corrected in #1706. |
| A07 | L143/L145/L151/L157/L209/L219: withdrawn/disproved review claims about nested types, nullable warnings, availability and repair-by-replacement | Supported reported resolution, not new defect work. F/R lifecycle verification preserves forward reads and supplying missing handles; duplicate replacement remains rejected. |
| A08 | L159: BroadcastChannel compile deadline, unchanged job passes retry | No root cause/fix established. R15 owns the specific compilation/reuse boundary; original log unavailable under M03. |
| A09 | L171/L269/L382/L445/L617/L687: sandbox HTTP/IPC/certificate/log access failures, paired unchanged controls pass with required access | Historical environment/setup outcomes; preserve original failures, do not count restricted attempts as semantic passes or change deadlines. M03 records missing logs. |
| A10 | L183/L465/L517/L667: aggressive/default parallel timing failures and affinity/bounded-thread controls | B14; neither successful isolated controls nor reduced local parallelism diagnose all historical failures. |
| A11 | L211: feature fixture used external literal; loopback correction | Completed hermetic test-data repair; product/compiler output unchanged. B14 separately retains interpreted generator timeout. |
| A12 | L219/L227/L249/L269/L279: missing runtimeconfig/PDB/dependency setup, reference preparation, forced-standalone runtime-copy assertions | Completed preparation corrections with original rejected attempts retained. Runtimeconfig and optional-PDB absence are harness conditions; R05/F17 retain deployment. |
| A13 | L227: dynamic-name bridge assertion incorrectly required direct SharpTS reference; restricted test builds stopped without error | Existing by-name dependency contract verified by reported saved controls; assertion corrected. Build root cause remains unestablished, missing logs M03; no invented production fix. |
| A14 | L237/L239: using-loop invalid IL and raw CLR Action rejected where a callable wrapper is required | Invalid using IL is #1786. Corrected native TSFunction/receiver/symbol/IDisposable controls credit retained R01/R02 callable/disposal contract; raw Action failure does not establish a guest-source defect. |
| A15 | L247/L249: nine enum StackUnexpected assemblies; preview dependency overlay, absent PDB, case-insensitive result classification | Invalid IL stays #1791; completed harness corrections preserve all identities and 176 additional cases. F13 retains enum caller metadata. |
| A16 | L259: deliberately omitted receiver rejected by TypeScript arity checking | Corrected reference carries expected diagnostic, same runtime expression/output/deadline. R03 receiver contract retained; no new source conformance claim. |
| A17 | L291: native harness deadlines including uncaptured first stderr | Completed corrected harness run under original limit; first stderr is explicitly unrecoverable, M03. No guessed reason for that attempt. |
| A18 | L293: unchanged Worker/MessagePort collectible realm alive after GC in first Windows CI; identical-head retry/Linux/local controls pass | Cause unestablished; R15 owns finite realm-lifetime verification and original-evidence recovery limit. No worker repair claimed. |
| A19 | L319: All_ResultAssimilatesArrayPrototypeThen compiled output empty; identical-head retry and saved/local controls pass | Cause unestablished; R08 owns this specific Promise/checkpoint observation. Missing original failed log M03 prevents assigning cause. |
| A20 | L348/L352/L372: blocking-task warning, wrong result manifest, ARM64 GitHub checkout outage, review cooldown, unique truncated-name evidence check | Completed preparation/service outcomes. Corrected verifier uses test-ID/name pairs and exact multiplicities; earlier runs are not accepted replacement evidence. |
| A21 | L380/L382/L390: TLS labels violate hermetic guard, intermediate path mistake, development insertion error, guessed protocol harness aborted before saving results | Completed setup/test-data corrections. S19 uses measured Node state, not the guessed expectation; stopped partial TRX and original failures remain distinct. |
| A22 | L455: Zstd saved IL fails without documented ZstdSharp.dll sidecar | Retained external dependency contract, corrected controls with the same sidecar pass; R14/R05/F17. No SharpTS runtime dependency or product fix inferred. |
| A23 | L579: CommonJS source incorrectly compared as ESM .ts; Node also rejects require | Completed reference correction using identical source bytes in .cts. It does not repair B20's distinct constructor aliases. |
| A24 | L589/L609/L627/L643/L659/L679: feature-detector expectation, JSON reserialization/source-path comparison, namespace/type typo, output preservation guard, duplicate-local/field-type assumptions | Completed bounded harness/test setup corrections; exact baseline source paths/report values and carrier assertions retained. R04/R05/F16 own actual contracts. |
| A25 | L685/L689/L695–697: CLR CreateType retry can return null; source-only union probe lacks actual carrier; custom interface abstract members unsupported; AOT seam warning | #1833's checked null result/interface contract and TypeProvider seam corrections are credited. Direct generated conversion probes supply real carrier coverage. Earlier broad verification belongs to its earlier head; F14 retains union configuration. |
| A26 | C10/C13: lifecycle-test setup/rest packing fix; hosted top-level-await exceeds dispatcher 10,000 turns, rerun passes | Private/rest correction is S22; #1856's unchanged hosting rerun does not establish timeout cause. R05 owns the specific hosted execution boundary, M03 its missing logs. |
| A27 | L403: possibly-undefined descriptor probe rejected; non-null control passes | Typing/control limitation, exact original source unavailable. Finite recovery/reference-diagnostic check stays with [#1769](https://github.com/nickna/SharpTS/issues/1769) and R03; do not declare a second setter defect. |
| A28 | L403: unsupported Function source rejection | Closed [#1614](https://github.com/nickna/SharpTS/issues/1614) is the historical contract reference. The exact rejected source is unavailable; R01 (#1868) owns a finite recovery/current-contract comparison, not reopening #1614 or inferring that indirect eval's #1771 is fixed. |
| A29 | L405: two direct URI calls rejected under checked-in TypeScript signatures; any-typed calls pass | Original exact source/diagnostic missing. [#1767](https://github.com/nickna/SharpTS/issues/1767) owns finite recovery and comparison with original checked declarations, separately from its four runtime discrepancies. |
| A30 | L413: typed Symbol.iterator array assignment rejected; raw CLR LINQ enumeration uninitialized | [#1752](https://github.com/nickna/SharpTS/issues/1752) includes TypeScript reference validation; [#1754](https://github.com/nickna/SharpTS/issues/1754) is native-only and does not establish guest-source failure. |
| A31 | L419: top-level-await control rejected without an assembly | Control/frontend limitation, not a saved failing runtime. Recover reference/module settings within [#1740](https://github.com/nickna/SharpTS/issues/1740)/R05 before attributing a second iterator defect. |
| A32 | L421/L423/L427/L429 and enum/namespace/using paragraphs: rejected source and 30-second process deadlines after uncaught errors | Keep diagnostics/no-assembly distinct from invalid IL, failed output and timeout. Specific issues below retain each program; [#1772](https://github.com/nickna/SharpTS/issues/1772) owns shutdown cause. No root cause is inferred from normal IL verification. |
| A33 | L809: eight write-only stores removed but emitted helpers still exist once | Credit JSON/Symbol/RegExp/iterator migration storage removal; R02/R03/R04/R06 retain generated helper ABI/body obligations. Old 393/350 counts are historical navigation, not current defects. |
| A34 | L386/L521/L535/L537/L602/L699/L707/L715/L811/L835: changing counts and wider inventories, including generated files and syntactic aliases | Retain baseline provenance and distinct dimensions. Partial early inventories recovered under M02 do not authenticate the later complete ledger; F01–F17 and R01–R15 own named residual boundaries. |
| A35 | C02–C07: open phase then merged phase; empty-list fast path and computed accessor canonical revisits | Credit #1850–1852 checked declaration/body completion and both pipeline boundaries; pending CI statements are superseded only by corresponding merged comment evidence. F01/F02 retain final ownership verification. |

## Original eighteen comments

The table includes every historical comment, including both pending and merged reports.
Each URL/hash also appears in the ledger. No later publication comment supplies historical proof.

| ID | Original comment / phase | Dispositions and outstanding destinations |
| --- | --- | --- |
| C01 | [5758587925](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5758587925) — #1849 merged | S21, B07, B08, F01; all broader named residual boundaries remain F01–F17/R01–R15. |
| C02 | [5758864530](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5758864530) — #1850 pending | F01, B07, B08, A35; all broader named residual boundaries remain F01–F17/R01–R15. |
| C03 | [5759197635](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5759197635) — #1850 merged | F01, B07, B08, A35; all broader named residual boundaries remain F01–F17/R01–R15. |
| C04 | [5759511563](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5759511563) — #1851 pending | F02, B07, B08, A35; all broader named residual boundaries remain F01–F17/R01–R15. |
| C05 | [5759764687](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5759764687) — #1851 merged | F02, B07, B08, A35; all broader named residual boundaries remain F01–F17/R01–R15. |
| C06 | [5759987177](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5759987177) — #1852 pending | F02, A35; all broader named residual boundaries remain F01–F17/R01–R15. |
| C07 | [5851304840](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5851304840) — #1852 merged; private next | F02, S22, A35; all broader named residual boundaries remain F01–F17/R01–R15. |
| C08 | [5851436486](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5851436486) — #1853 pending | F02, S22, B03, B04, B05; all broader named residual boundaries remain F01–F17/R01–R15. |
| C09 | [5851663937](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5851663937) — #1853 merged | F02, S22, B03, B04, B05; all broader named residual boundaries remain F01–F17/R01–R15. |
| C10 | [5851883354](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5851883354) — #1854 pending | S22, B02, B03, B05, B09, A26; all broader named residual boundaries remain F01–F17/R01–R15. |
| C11 | [5852145767](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5852145767) — #1854 merged | S22, F02, B02, B03, B05, B09; all broader named residual boundaries remain F01–F17/R01–R15. |
| C12 | [5852417894](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5852417894) — #1855 merged | F01, F02, S23; all broader named residual boundaries remain F01–F17/R01–R15. |
| C13 | [5853151814](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5853151814) — #1856 merged | S23, S24, B02, A26; all broader named residual boundaries remain F01–F17/R01–R15. |
| C14 | [5853772974](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5853772974) — #1857 merged | S24, S25, B06; all broader named residual boundaries remain F01–F17/R01–R15. |
| C15 | [5854752632](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5854752632) — #1858 merged | S25, S26, B06, B09; all broader named residual boundaries remain F01–F17/R01–R15. |
| C16 | [5856384685](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5856384685) — #1859 merged | S26, S27; all broader named residual boundaries remain F01–F17/R01–R15. |
| C17 | [5857162238](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5857162238) — #1860 merged | S27, S28; all broader named residual boundaries remain F01–F17/R01–R15. |
| C18 | [5858750427](https://github.com/nickna/SharpTS/issues/1599#issuecomment-5858750427) — #1861 merged | S28, B01; all broader named residual boundaries remain F01–F17/R01–R15. |

## Local artifact recovery and limits

This pass searched the existing checkout's tracked paths and the ignored `artifacts/`,
`.perf-runs/`, `.codex/`, `.agents/`, `.claude/` and `scripts/` paths once. Build outputs and
the old `main-check` checkout copy were excluded from the artifact-name census; the search
does not reach other machines or invent `/tmp` paths. Source/tests at the frozen Git commit
are recoverable. The machine ledger records hashes for recovered files and exact missing names.

| ID | Recovery result | What it establishes / what remains unavailable |
| --- | --- | --- |
| M01 | Recovered exact frozen body and eighteen comments from GitHub; recovered frozen ARCHITECTURE.md and all named production/regression sources from Git | Exact historical input identity and source-level comparisons. Reported CI/Node/Linux output is historical evidence unless this pass explicitly reran it. |
| M02 | Recovered `artifacts/1599-arraybuffer/inventory.json`, `1599-buffer/inventory.json`, `1599-fetch/inventory.json`, `1599-http/inventory.json`, plus `.perf-runs/1599-array-operations/mapping.json` and early phase source/output/log artifacts | These are earlier family mapping inventories, not the 1,160-declaration/393-construction ledger, later 126-root ownership disposition, or expanded 178,501/178,589-reference census. They cannot certify current residual owners, indirect writes, identity/lifetime or complete case multiplicities. |
| M03 | Not recovered: `eventemitter-construction-guest-results-v1.json`, `eventemitter-construction-guest-results-v2.json`, `event-capture-pending-v1.json`, `fetch-construction-guest-results-v1.json` and its unnamed v2 continuation, `module-registry-ownership-fetch-v1.json`, `module-registry-dns-duplicate-audit-v1.json`; `artifacts/headers-conversion-cross-platform-v2/results.json`, `artifacts/dns-lookup-cross-platform-v3/results.json`, `artifacts/withresolvers-output-v1/results.json`, `artifacts/event-loop-cancellation-output-v2/results.json`; unnamed final source census/ledgers, per-head review/build/CI/IL manifests and original failure bundles cited without a location | Cannot independently reproduce their historical compiler/assembly hashes, complete metadata/body comparisons, exact original runtime sources not embedded in issues/tests, or original failure causes. S rows distinguish matching committed regressions from unavailable historical artifacts; A/B/R/F rows provide concrete destinations for remaining investigation. Missing first-attempt stderr in A17 was never captured and cannot be recovered from a later success. |
| M04 | New evidence: Release build, focused TRX, exact-source #1805 standalone regressions and ledger verifier | Results are local Windows checks at the frozen source baseline plus the added test. They do not replace missing historical bundles or establish fresh Linux/Node/whole-suite/CI/review outcomes. |

Missing evidence is a completed recovery outcome for H01. Its specific observation now has
a supported retained/repair disposition or finite linked child investigation. No remaining
historical failure is tracked only by an inaccessible local note, and recovering every old
log is not a prerequisite to finishing this finite pass.

## Validation and completion

The Release test-project build passed with zero errors and one existing NU1902 package
warning. The focused normal-access run passed **572/572 cases**, with no failures or skips.
It covers the named supersession regressions, including all three exact #1805 CLI/standalone
sources, both-mode local-class controls, hosted/ordinary runtime IL/lifecycle checks,
JSON's sensitive descriptor assertion, dependency opposite-selection controls, and the
specific stream/fetch/DNS/X509/cancellation/EventEmitter/TLS/generic-class expectations.

The first restricted run is retained: **483 passed, 10 failed, 493 total**. All ten failed
at loopback HTTP listener fixture setup (`The handle is invalid.`), before those programs
ran. The normal-access run retains all 493 original test-ID/name pairs and passes each,
plus 79 additional JSON scalar-record and class-expression cases after correcting the
selection to their actual class names. Product/test assertions and deadlines did not change
between the two runs. This does not turn the restricted failures into conformance results.

The ledger records the exact final filter, TRX hashes, counts, and the three #1805 source
hashes and successful test identities. TRX files remain in ignored local evidence paths
`artifacts/issue-1926/tests/` and `artifacts/issue-1926/tests-normal-access/`; they are not
portable committed result bundles. No fresh Node, Linux, full core suite, CI, AOT or external
review run is claimed. Shipping compiler/runtime source is unchanged.

The [coverage verifier](../../../scripts/verify-1599-reconciliation.ps1) passes against the
downloaded frozen response and the five recovered mapping inventories. It checks the
frozen body hash/length, all **459 nonempty source lines**, **18 original comments**,
**63 existing semantic destinations**, **26 supersession candidates**, valid report-row
references, and recovered-file hashes. The final diff check passes.
It also checks the frozen architecture hash and exact equality between the three recorded
#1805 sources and their permanent standalone regression data.

To reproduce coverage after saving a GitHub response containing `body`, `comments` and
`url`, run from the repository root:

```powershell
pwsh -NoProfile -File scripts/verify-1599-reconciliation.ps1 -IssueJson artifacts/issue-1926/issue-1599.json
```

Add `-VerifyLocalArtifacts` only when the historical local inventories are present.
The verifier reads evidence and does not fetch issues or modify their state.

This completes the one-pass reconciliation boundary. The original
records and historical failures remain preserved. The runtime/ownership children, 63 existing
semantic reports and B01–B26 remain independently completable work; H01 does not require their
implementation, recursively audit their newly referenced sources, or search for arbitrary bugs.
