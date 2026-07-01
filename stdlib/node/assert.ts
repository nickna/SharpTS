// Node.js 'assert' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/assert.html.
//
// Pure-logic leaf. No host dependencies: assertions are just value
// comparisons that throw AssertionError on failure. Node's assert API
// is wide but individually each method is small.

/**
 * Error class thrown by assert.* when an assertion fails. Mirrors Node's
 * AssertionError shape (actual/expected/operator/generatedMessage fields
 * and code='ERR_ASSERTION') so user code that inspects the error works
 * identically.
 */
export class AssertionError extends Error {
    actual: any;
    expected: any;
    operator: string;
    generatedMessage: boolean;
    code: string;

    // options: { message?, actual?, expected?, operator?, stackStartFn? }.
    // Typed `any` because the type checker treats an inline optional-property
    // object param as all-required at construction sites (and `options || {}`
    // would otherwise widen to `{}`, dropping `.message`).
    constructor(options?: any) {
        const opts: any = options || {};
        const generated = opts.message == null;
        const msg = opts.message != null ? opts.message : 'AssertionError';
        super(msg);
        this.name = 'AssertionError';
        this.actual = opts.actual;
        this.expected = opts.expected;
        this.operator = opts.operator != null ? opts.operator : '';
        this.generatedMessage = generated;
        this.code = 'ERR_ASSERTION';
    }
}

// ─── Value stringification (for generated error messages) ───────────

function stringify(value: any): string {
    if (value === null) return 'null';
    if (value === undefined) return 'undefined';
    const t = typeof value;
    if (t === 'string') return '"' + value + '"';
    if (t === 'number' || t === 'boolean' || t === 'bigint') return String(value);
    if (t === 'function') return '[Function]';
    if (t === 'symbol') return String(value);
    if (Array.isArray(value)) {
        const items: string[] = [];
        for (let i = 0; i < value.length; i++) items.push(stringify(value[i]));
        return '[' + items.join(', ') + ']';
    }
    // Plain object
    try {
        const keys = Object.keys(value);
        const parts: string[] = [];
        for (const k of keys) parts.push(k + ': ' + stringify(value[k]));
        return '{' + parts.join(', ') + '}';
    } catch {
        return String(value);
    }
}

// ─── Equality primitives ────────────────────────────────────────────

function sameValue(a: any, b: any): boolean {
    // Node's strict* family uses SameValue semantics (Object.is), which
    // treats NaN as equal to NaN and -0 as distinct from +0. Plain ===
    // diverges on NaN.
    if (a === b) {
        // +0 / -0 check: 1/+0 === Infinity, 1/-0 === -Infinity.
        if (a === 0 && b === 0) return 1 / (a as number) === 1 / (b as number);
        return true;
    }
    // NaN: only value that's not equal to itself.
    return a !== a && b !== b;
}

function looseEquals(a: any, b: any): boolean {
    // JS abstract (`==`) equality, implemented explicitly: SharpTS's own `==`
    // does not coerce across number/string/boolean, so relying on it would make
    // the loose family behave like the strict one.
    if (a === b) return true;
    if ((a === null || a === undefined) && (b === null || b === undefined)) return true;
    const ta = typeof a;
    const tb = typeof b;
    if (ta === tb) return a === b;
    // number <-> string
    if (ta === 'number' && tb === 'string') return a === Number(b);
    if (ta === 'string' && tb === 'number') return Number(a) === b;
    // bigint <-> string / number
    if (ta === 'bigint' && tb === 'string') { try { return a === BigInt(b); } catch (e) { return false; } }
    if (ta === 'string' && tb === 'bigint') { try { return BigInt(a) === b; } catch (e) { return false; } }
    if (ta === 'bigint' && tb === 'number') return Number(a) === b;
    if (ta === 'number' && tb === 'bigint') return a === Number(b);
    // boolean coerces to number, then re-compare
    if (ta === 'boolean') return looseEquals(a ? 1 : 0, b);
    if (tb === 'boolean') return looseEquals(a, b ? 1 : 0);
    return false;
}

function deepEquals(a: any, b: any, strict: boolean): boolean {
    // Primitive / null fast path. For any type where SameValue (strict) or
    // loose equality suffices, we don't need to recurse into structure.
    if (typeof a !== 'object' || a === null || typeof b !== 'object' || b === null) {
        return strict ? sameValue(a, b) : looseEquals(a, b);
    }

    // Reference identity (same object).
    if (a === b) return true;

    // Array branch: both must be arrays, same length, deep-equal elements.
    // Falling back to the object branch for arrays would also work in pure JS
    // (Object.keys on arrays returns the indices), but arrays get their own
    // branch for predictable element-by-index traversal.
    // Cast through `any`: the typeof/null guards above narrow a/b to `object`,
    // which the type checker rejects for numeric/string indexing.
    const av: any = a;
    const bv: any = b;
    const aIsArray = Array.isArray(a);
    const bIsArray = Array.isArray(b);
    if (aIsArray !== bIsArray) return false;
    if (aIsArray) {
        const lenA = av.length;
        const lenB = bv.length;
        if (lenA !== lenB) return false;
        for (let i = 0; i < lenA; i++) {
            if (!deepEquals(av[i], bv[i], strict)) return false;
        }
        return true;
    }

    // Plain objects: same keys, deep-equal values.
    const keysA = Object.keys(a);
    const keysB = Object.keys(b);
    if (keysA.length !== keysB.length) return false;
    for (const key of keysA) {
        if (!(key in bv)) return false;
        if (!deepEquals(av[key], bv[key], strict)) return false;
    }
    return true;
}

// ─── Message helpers ────────────────────────────────────────────────

function resolveMessage(message: any, fallback: string): string {
    if (message == null) return fallback;
    if (typeof message === 'string') return message;
    // Node also accepts an Error instance directly, but we narrow to string here —
    // the full Error-passthrough variant is deferred until a test demands it.
    return fallback;
}

function fail_(message: string, actual: any, expected: any, op: string): never {
    throw new AssertionError({
        message,
        actual,
        expected,
        operator: op,
    });
}

// ─── Public API ─────────────────────────────────────────────────────

/** Throws if `value` is falsy. */
export function ok(value: any, message?: string | Error): void {
    if (!value) {
        fail_(
            resolveMessage(message, 'The expression evaluated to a falsy value'),
            value,
            true,
            'ok'
        );
    }
}

/** Throws if `actual !== expected` (SameValue). */
export function strictEqual(actual: any, expected: any, message?: string | Error): void {
    if (!sameValue(actual, expected)) {
        fail_(
            resolveMessage(message,
                'Expected values to be strictly equal:\n' + stringify(actual) +
                '\nshould equal\n' + stringify(expected)),
            actual,
            expected,
            'strictEqual'
        );
    }
}

/** Throws if `actual === expected` (SameValue). */
export function notStrictEqual(actual: any, expected: any, message?: string | Error): void {
    if (sameValue(actual, expected)) {
        fail_(
            resolveMessage(message,
                'Expected values to be strictly unequal: ' + stringify(actual)),
            actual,
            expected,
            'notStrictEqual'
        );
    }
}

/** Deep comparison with strict (SameValue) equality at leaves. */
export function deepStrictEqual(actual: any, expected: any, message?: string | Error): void {
    if (!deepEquals(actual, expected, true)) {
        fail_(
            resolveMessage(message,
                'Expected values to be deeply equal:\n' + stringify(actual) +
                '\nshould equal\n' + stringify(expected)),
            actual,
            expected,
            'deepStrictEqual'
        );
    }
}

/** Throws if actual and expected are deeply strictly equal. */
export function notDeepStrictEqual(actual: any, expected: any, message?: string | Error): void {
    if (deepEquals(actual, expected, true)) {
        fail_(
            resolveMessage(message,
                'Expected values not to be deeply equal: ' + stringify(actual)),
            actual,
            expected,
            'notDeepStrictEqual'
        );
    }
}

/** Loose (`==`) equality. */
export function equal(actual: any, expected: any, message?: string | Error): void {
    if (!looseEquals(actual, expected)) {
        fail_(
            resolveMessage(message,
                'Expected values to be loosely equal:\n' + stringify(actual) +
                '\nshould equal\n' + stringify(expected)),
            actual,
            expected,
            'equal'
        );
    }
}

/** Loose (`!=`) inequality. */
export function notEqual(actual: any, expected: any, message?: string | Error): void {
    if (looseEquals(actual, expected)) {
        fail_(
            resolveMessage(message,
                'Expected values not to be loosely equal: ' + stringify(actual)),
            actual,
            expected,
            'notEqual'
        );
    }
}

/** Always throws; convenience for unreachable branches. */
export function fail(message?: string | Error): never {
    fail_(resolveMessage(message, 'Failed'), undefined, undefined, 'fail');
    // Unreachable — fail_ returns never — but TS requires an explicit return
    // on some code paths. The throw above handles it.
    throw new AssertionError({ message: 'unreachable' });
}

/** Throws if `fn` does NOT throw when invoked. */
export function throws(fn: Function, message?: string | Error): void {
    if (typeof fn !== 'function') {
        fail_('First argument must be a function', fn, undefined, 'throws');
    }
    let threw = false;
    try {
        fn();
    } catch {
        threw = true;
    }
    if (!threw) {
        fail_(
            resolveMessage(message, 'Missing expected exception'),
            undefined,
            undefined,
            'throws'
        );
    }
}

/** Throws if `fn` DOES throw when invoked. */
export function doesNotThrow(fn: Function, message?: string | Error): void {
    if (typeof fn !== 'function') {
        fail_('First argument must be a function', fn, undefined, 'doesNotThrow');
    }
    try {
        fn();
    } catch (e) {
        const base = 'Got unwanted exception';
        const detail = (e as any) && (e as any).message ? ': ' + (e as any).message : '';
        fail_(
            resolveMessage(message, base + detail),
            e,
            undefined,
            'doesNotThrow'
        );
    }
}

// ─── Error matching (shared by throws/rejects with an expectation) ──────

function errorMatches(actual: any, expected: any): boolean {
    if (expected == null) return true;
    if (expected instanceof RegExp) {
        const subject = (actual != null && actual.message != null) ? String(actual.message) : String(actual);
        return expected.test(subject);
    }
    if (typeof expected === 'function') {
        // An Error subclass constructor → instanceof check; otherwise treat it
        // as a validation function whose truthy result signals a match.
        if (actual instanceof expected) return true;
        try {
            return expected(actual) === true;
        } catch (e) {
            return false;
        }
    }
    if (typeof expected === 'object') {
        const keys = Object.keys(expected);
        for (const k of keys) {
            if (actual == null || actual[k] !== expected[k]) return false;
        }
        return true;
    }
    return false;
}

// When the caller passes (subject, message) with no error matcher, the second
// argument is the message. Distinguishes it from an error expectation.
function normalizeErrorAndMessage(error: any, message: any): any[] {
    if (typeof error === 'string' && message === undefined) {
        return [undefined, error];
    }
    return [error, message];
}

// ─── async ─────────────────────────────────────────────────────────────

/** Awaits `asyncFn` (called) or a promise and throws if it does NOT reject. */
export async function rejects(asyncFnOrPromise: any, error?: any, message?: string | Error): Promise<void> {
    const parts = normalizeErrorAndMessage(error, message);
    const expected = parts[0];
    const msg = parts[1];

    const promise = typeof asyncFnOrPromise === 'function' ? asyncFnOrPromise() : asyncFnOrPromise;
    let threw = false;
    let actualErr: any;
    try {
        await promise;
    } catch (e) {
        threw = true;
        actualErr = e;
    }
    if (!threw) {
        fail_(resolveMessage(msg, 'Missing expected rejection'), undefined, undefined, 'rejects');
    }
    if (expected != null && !errorMatches(actualErr, expected)) {
        fail_(resolveMessage(msg, 'Rejection did not match the expected error'), actualErr, expected, 'rejects');
    }
}

/** Awaits `asyncFn` (called) or a promise and throws if it DOES reject. */
export async function doesNotReject(asyncFnOrPromise: any, error?: any, message?: string | Error): Promise<void> {
    const parts = normalizeErrorAndMessage(error, message);
    const msg = parts[1];

    const promise = typeof asyncFnOrPromise === 'function' ? asyncFnOrPromise() : asyncFnOrPromise;
    let actualErr: any;
    let threw = false;
    try {
        await promise;
    } catch (e) {
        threw = true;
        actualErr = e;
    }
    if (threw) {
        const detail = (actualErr != null && actualErr.message != null) ? ': ' + actualErr.message : '';
        fail_(resolveMessage(msg, 'Got unwanted rejection' + detail), actualErr, undefined, 'doesNotReject');
    }
}

// ─── match / doesNotMatch ───────────────────────────────────────────────

/** Throws if `regexp` does not match `str`. */
export function match(str: any, regexp: any, message?: string | Error): void {
    if (!(regexp instanceof RegExp)) {
        fail_('The "regexp" argument must be an instance of RegExp', regexp, undefined, 'match');
    }
    if (typeof str !== 'string') {
        fail_('The "string" argument must be of type string', str, undefined, 'match');
    }
    if (!regexp.test(str)) {
        fail_(
            resolveMessage(message, 'The input did not match the regular expression ' + String(regexp)),
            str, regexp, 'match');
    }
}

/** Throws if `regexp` matches `str`. */
export function doesNotMatch(str: any, regexp: any, message?: string | Error): void {
    if (!(regexp instanceof RegExp)) {
        fail_('The "regexp" argument must be an instance of RegExp', regexp, undefined, 'doesNotMatch');
    }
    if (typeof str !== 'string') {
        fail_('The "string" argument must be of type string', str, undefined, 'doesNotMatch');
    }
    if (regexp.test(str)) {
        fail_(
            resolveMessage(message, 'The input was expected to not match the regular expression ' + String(regexp)),
            str, regexp, 'doesNotMatch');
    }
}

// ─── ifError ────────────────────────────────────────────────────────────

/** Throws `value` if it is not null/undefined (for Node-style error callbacks). */
export function ifError(value: any): void {
    if (value === null || value === undefined) return;
    const detail = (value != null && (value as any).message != null) ? String((value as any).message) : stringify(value);
    fail_('ifError got unwanted exception: ' + detail, value, null, 'ifError');
}

// ─── loose deep (in)equality ────────────────────────────────────────────

/** Deep comparison with loose (`==`) equality at leaves. */
export function deepEqual(actual: any, expected: any, message?: string | Error): void {
    if (!deepEquals(actual, expected, false)) {
        fail_(
            resolveMessage(message,
                'Expected values to be loosely deep-equal:\n' + stringify(actual) +
                '\nshould loosely deep-equal\n' + stringify(expected)),
            actual, expected, 'deepEqual');
    }
}

/** Throws if actual and expected are loosely deep-equal. */
export function notDeepEqual(actual: any, expected: any, message?: string | Error): void {
    if (deepEquals(actual, expected, false)) {
        fail_(
            resolveMessage(message,
                'Expected values not to be loosely deep-equal: ' + stringify(actual)),
            actual, expected, 'notDeepEqual');
    }
}

// ─── partialDeepStrictEqual ─────────────────────────────────────────────
//
// `actual` must deep-strict-*contain* `expected`: every property/element in
// expected has a deep-strict-equal counterpart in actual (Node 22+).

function partialDeepEquals(actual: any, expected: any): boolean {
    if (typeof expected !== 'object' || expected === null) {
        return sameValue(actual, expected);
    }
    if (typeof actual !== 'object' || actual === null) return false;
    if (actual === expected) return true;

    if (Array.isArray(expected)) {
        if (!Array.isArray(actual)) return false;
        const av: any = actual;
        const ev: any = expected;
        // Each expected element must partial-match some actual element.
        for (let i = 0; i < ev.length; i++) {
            let found = false;
            for (let j = 0; j < av.length; j++) {
                if (partialDeepEquals(av[j], ev[i])) { found = true; break; }
            }
            if (!found) return false;
        }
        return true;
    }
    if (Array.isArray(actual)) return false;

    const av: any = actual;
    const ev: any = expected;
    const keys = Object.keys(expected);
    for (let i = 0; i < keys.length; i++) {
        const k = keys[i];
        if (!(k in av)) return false;
        if (!partialDeepEquals(av[k], ev[k])) return false;
    }
    return true;
}

/** Throws unless `actual` deep-strict-contains `expected`. */
export function partialDeepStrictEqual(actual: any, expected: any, message?: string | Error): void {
    if (!partialDeepEquals(actual, expected)) {
        fail_(
            resolveMessage(message,
                'Expected actual to partially deep-strict-equal expected:\n' + stringify(actual) +
                '\nshould contain\n' + stringify(expected)),
            actual, expected, 'partialDeepStrictEqual');
    }
}

// ─── CallTracker ────────────────────────────────────────────────────────
//
// Deprecated in Node, but the documented surface is supported: calls/getCalls/
// report/verify/reset.

interface TrackedCall {
    fn: any;
    exact: number;
    wrapper: any;
    calls: any[];
}

/** Tracks how many times wrapped functions are called (Node's assert.CallTracker). */
export class CallTracker {
    private _tracked: TrackedCall[];

    constructor() {
        this._tracked = [];
    }

    /**
     * Wraps `fn` (or a no-op) and expects the wrapper to be called `exact`
     * times (default 1). Returns the wrapper.
     */
    calls(fn?: any, exact?: number): any {
        let actualFn = fn;
        let expected = exact;
        if (typeof fn === 'number') { expected = fn; actualFn = undefined; }
        if (expected === undefined || expected === null) expected = 1;

        const entry: TrackedCall = { fn: actualFn, exact: expected, wrapper: null, calls: [] };
        const wrapper = (...args: any[]): any => {
            entry.calls.push({ thisArg: undefined, arguments: args });
            if (typeof actualFn === 'function') return actualFn.apply(undefined, args);
            return undefined;
        };
        entry.wrapper = wrapper;
        this._tracked.push(entry);
        return wrapper;
    }

    /** The recorded calls (`{ thisArg, arguments }`) for a wrapper. */
    getCalls(fn: any): any[] {
        for (let i = 0; i < this._tracked.length; i++) {
            if (this._tracked[i].wrapper === fn) return this._tracked[i].calls;
        }
        return [];
    }

    /** Info objects for every wrapper whose call count is off. */
    report(): any[] {
        const out: any[] = [];
        for (let i = 0; i < this._tracked.length; i++) {
            const e = this._tracked[i];
            if (e.calls.length !== e.exact) {
                out.push({
                    message: 'Expected the function to be called ' + e.exact +
                        ' time(s) but it was called ' + e.calls.length + ' time(s).',
                    actual: e.calls.length,
                    expected: e.exact,
                    operator: (e.fn != null && e.fn.name) ? e.fn.name : 'calls',
                });
            }
        }
        return out;
    }

    /** Throws an AssertionError if any wrapper's call count is off. */
    verify(): void {
        const r = this.report();
        if (r.length > 0) {
            fail_('Expected all tracked functions to be called the specified number of times',
                r.length, 0, 'verify');
        }
    }

    /** Resets recorded calls — for one wrapper, or all when called with no arg. */
    reset(fn?: any): void {
        if (fn === undefined) {
            for (let i = 0; i < this._tracked.length; i++) this._tracked[i].calls = [];
            return;
        }
        for (let i = 0; i < this._tracked.length; i++) {
            if (this._tracked[i].wrapper === fn) this._tracked[i].calls = [];
        }
    }
}

// ─── Callable module ────────────────────────────────────────────────────
//
// Node's `assert` export is itself callable: `assert(value[, message])` is an
// alias for `assert.ok`. The default export is therefore a function object
// carrying every assert.* member.

function assert(value: any, message?: string | Error): void {
    ok(value, message);
}

// The `strict` namespace: itself callable (=== ok), with the loose forms
// aliased to their strict counterparts and the full member set attached.
function strictAssert(value: any, message?: string | Error): void {
    ok(value, message);
}
const strict: any = strictAssert;
strict.AssertionError = AssertionError;
strict.ok = ok;
strict.strictEqual = strictEqual;
strict.notStrictEqual = notStrictEqual;
strict.deepStrictEqual = deepStrictEqual;
strict.notDeepStrictEqual = notDeepStrictEqual;
strict.equal = strictEqual;                 // strict-mode alias
strict.notEqual = notStrictEqual;           // strict-mode alias
strict.deepEqual = deepStrictEqual;         // strict-mode alias
strict.notDeepEqual = notDeepStrictEqual;   // strict-mode alias
strict.fail = fail;
strict.throws = throws;
strict.doesNotThrow = doesNotThrow;
strict.rejects = rejects;
strict.doesNotReject = doesNotReject;
strict.match = match;
strict.doesNotMatch = doesNotMatch;
strict.ifError = ifError;
strict.partialDeepStrictEqual = partialDeepStrictEqual;
strict.CallTracker = CallTracker;
strict.strict = strict;
export { strict };

const assertModule: any = assert;
assertModule.AssertionError = AssertionError;
assertModule.ok = ok;
assertModule.strictEqual = strictEqual;
assertModule.notStrictEqual = notStrictEqual;
assertModule.deepStrictEqual = deepStrictEqual;
assertModule.notDeepStrictEqual = notDeepStrictEqual;
assertModule.deepEqual = deepEqual;
assertModule.notDeepEqual = notDeepEqual;
assertModule.equal = equal;
assertModule.notEqual = notEqual;
assertModule.fail = fail;
assertModule.throws = throws;
assertModule.doesNotThrow = doesNotThrow;
assertModule.rejects = rejects;
assertModule.doesNotReject = doesNotReject;
assertModule.match = match;
assertModule.doesNotMatch = doesNotMatch;
assertModule.ifError = ifError;
assertModule.partialDeepStrictEqual = partialDeepStrictEqual;
assertModule.CallTracker = CallTracker;
assertModule.strict = strict;

export default assertModule;
