# Proxy constructor programs (#1799)

The four original sources remain unchanged: `constructor-target.ts` prints
`8`, `aliased-constructor.ts` prints `9`, and the ordinary/dynamic class controls
each print `8` on Node. The two failing programs IL-verify on unchanged main
`0ad37b57`, deploy the matching SharpTS.dll, produce no stdout and report
`TypeError: not a constructor`. Fresh unchanged-main/current-before-repair probes
exit with code `-532462766` before the 30-second deadline. The issue's original
observations instead reached that deadline and were killed with recorded code
`-1`; those historical stderr/deadline observations remain authoritative for
their runs. No cause of their delayed shutdown is inferred from the error text.
Both original class controls still pass on unchanged main.

Proxy forwarding now uses general dynamic construction, which handles compiled
class Type tokens. The exact emitted CreateProxy factory is recognized as a
constructor, class targets retain callable branding for bind, and both engines
reject non-constructor Proxy targets before invoking a construct trap. The
interpreter's no-trap function forwarding uses its fresh-receiver Construct
protocol. Bound constructor forwarding also uses general dynamic construction.

Additional controls check argument order, nested/bound class proxies, default
prototype linkage and instanceof, function/revocable construction, revoked and
invalid targets, primitive trap results, thrown-object identity and recovery.
The construct trap receives the exact target, argument list and newTarget for
both new and Reflect.construct. The trap itself chooses the returned object's
prototype; no general Reflect.construct alternate-prototype repair is claimed.

Run positive fixtures independently:

```powershell
node --experimental-strip-types --no-warnings tests/fixtures/ProxyConstructors/constructor-target.ts
tsc --noEmit --skipLibCheck --target es2022 tests/fixtures/ProxyConstructors/constructor-target.ts
```

All nine positive fixtures match Node v25.5.0 and pass TypeScript 7.0.2.
Saved checks use `--no-tsconfig --compile <source> -o <output> --verify` with
normal deployment. Every Proxy output must receive the matching SharpTS.dll
(bytes compared); controls omit it. All nine verify IL, print the reference
expectation, exit zero and have empty stderr within the original 30-second
execution deadline. Compilation has a separate 60-second bound.

Two forced-standalone declaration checks preserve runtime omission, no SharpTS
assembly reference, IL verification and the existing CLI runtime-dependency
note. They are not counted as executable standalone Proxy conformance. Retained
#1715 callable Proxy checks and hosted declaration/deployment checks also pass;
hosted exports are not claimed as executed. Ignored `artifacts/epic-1866/1799-*`
logs preserve fresh observations, test setup corrections and verification.

The final selected run passes 206 of 207 tests with compiled IL verification.
The sole failure is the unchanged-main union-getter IL defect in
`ProxyTests.Proxy_HasTrap_TruthyCoercion(Compiled)` (MethodFallthrough at 77 and
StackUnderflow at 64), independently retained in
`artifacts/epic-1866/1715-baseline-union.log`. Required quality gates and the
actual AOT analyzer baseline pass with zero analyzer warnings.
