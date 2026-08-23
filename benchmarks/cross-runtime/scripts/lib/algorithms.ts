// Shared benchmark algorithms — the SINGLE source of these workloads.
//
// Consumed two ways, so the two benchmark systems measure identical code:
//   * the cross-runtime shell harness (benchmarks/cross-runtime/run-benchmarks.ps1) imports
//     these into its driver scripts and runs them under the SharpTS
//     interpreter, the SharpTS compiler, Node.js, and Bun;
//   * the BenchmarkDotNet suite (SharpTS.Microbenchmarks) embeds this file as a
//     resource, compiles it, and invokes the functions via reflection.
//
// Keep every export a plain `number -> number` function (no top-level
// execution) so the BDN harness can reflect them off the compiled $Program.

// ── Numeric / recursion / loops ──────────────────────────────────────────

// Recursion + function-call overhead.
export function fibonacci(n: number): number {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

// Tight arithmetic loop.
export function factorial(n: number): number {
    let result: number = 1;
    for (let i: number = 2; i <= n; i++) {
        result = result * i;
    }
    return result;
}

// Array allocation + nested loops (Sieve of Eratosthenes).
export function countPrimes(n: number): number {
    if (n <= 2) return 0;

    const isPrime: boolean[] = [];
    for (let i: number = 0; i < n; i++) {
        isPrime.push(true);
    }
    isPrime[0] = false;
    isPrime[1] = false;

    for (let i: number = 2; i * i < n; i++) {
        if (isPrime[i]) {
            for (let j: number = i * i; j < n; j = j + i) {
                isPrime[j] = false;
            }
        }
    }

    let count: number = 0;
    for (let i: number = 0; i < n; i++) {
        if (isPrime[i]) count = count + 1;
    }
    return count;
}

// ── Broadened workloads (interpreter vs compiler vs V8 diverge most here) ──

// String building + scanning: concatenation then a charCodeAt sweep.
export function stringWork(n: number): number {
    let s: string = "";
    for (let i: number = 0; i < n; i++) {
        s = s + "ab";
    }
    let sum: number = 0;
    for (let i: number = 0; i < s.length; i++) {
        sum = sum + s.charCodeAt(i);
    }
    return sum;
}

// Object allocation + property access: build small records, sum their fields.
export function objectWork(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const o = { x: i, y: i + 1 };
        sum = sum + o.x + o.y;
    }
    return sum;
}

// Closures: build an adder closure per iteration and invoke it.
export function closureWork(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const add = (a: number): number => a + i;
        sum = sum + add(i);
    }
    return sum;
}

// Array higher-order methods: map -> filter -> reduce pipeline.
export function arrayMethodWork(n: number): number {
    const arr: number[] = [];
    for (let i: number = 0; i < n; i++) {
        arr.push(i);
    }
    const doubled = arr.map((x: number): number => x * 2);
    const evens = doubled.filter((x: number): boolean => x % 4 === 0);
    return evens.reduce((acc: number, x: number): number => acc + x, 0);
}

// ── Builtin-heavy / allocation / data-parallel workloads ──────────────────
// These diverge most across SharpTS-compiled, V8 (Node), and JSC (Bun): JSON
// and typed-array kernels lean on hand-tuned engine builtins, binary-trees on
// the GC. Each still funnels to a `number` so the BDN harness can reflect it.

// Map insertion, lookup, and deletion with numeric keys and values. The
// checksum observes both successful lookups and deletes while leaving half of
// the entries live, so runtimes cannot discard any phase.
export function mapOperations(n: number): number {
    const map = new Map<number, number>();
    for (let i: number = 0; i < n; i++) {
        map.set(i, i * 3 + 1);
    }

    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const value = map.get(i);
        if (value !== null && value !== undefined) {
            sum = sum + value;
        }
    }

    let deleted: number = 0;
    for (let i: number = 0; i < n; i = i + 2) {
        if (map.delete(i)) {
            deleted = deleted + 1;
        }
    }
    return sum + deleted + map.size;
}

// Map iteration is kept separate from lookup so iterator materialization and
// entry-pair representation remain visible in the cross-runtime results.
export function mapIteration(n: number): number {
    const map = new Map<number, number>();
    for (let i: number = 0; i < n; i++) {
        map.set(i, i * 3 + 1);
    }

    let sum: number = 0;
    for (const entry of map) {
        sum = sum + entry[0] + entry[1];
    }
    return sum + map.size;
}

// Set insertion, lookup, and deletion. The even-number delete pass observes
// mutation and size independently from the preceding membership checks.
export function setOperations(n: number): number {
    const set = new Set<number>();
    for (let i: number = 0; i < n; i++) {
        set.add(i);
    }

    let found: number = 0;
    for (let i: number = 0; i < n; i++) {
        if (set.has(i)) {
            found = found + 1;
        }
    }

    let deleted: number = 0;
    for (let i: number = 0; i < n; i = i + 2) {
        if (set.delete(i)) {
            deleted = deleted + 1;
        }
    }
    return found + deleted + set.size;
}

// Set iteration has its own workload so it is not hidden by hash lookups.
export function setIteration(n: number): number {
    const set = new Set<number>();
    for (let i: number = 0; i < n; i++) {
        set.add(i);
    }

    let sum: number = 0;
    for (const value of set) {
        sum = sum + value;
    }
    return sum + set.size;
}

// JSON round-trip: build a record array, stringify, parse it back, sum a field.
// The single most common server hot path; V8/JSC JSON are hand-tuned C++.
export function jsonRoundTrip(n: number): number {
    const items: { id: number; name: string; value: number }[] = [];
    for (let i: number = 0; i < n; i++) {
        items.push({ id: i, name: "item-" + i, value: i * 3 - 1 });
    }
    const json: string = JSON.stringify({ items: items });
    const parsed = JSON.parse(json);
    const back: { id: number; name: string; value: number }[] = parsed.items;
    let sum: number = 0;
    for (let i: number = 0; i < back.length; i++) {
        sum = sum + back[i].value;
    }
    return sum;
}

// Cumulative phase probes for the JSON round-trip regression guard.  Keeping
// each prefix byte-for-byte equivalent to jsonRoundTrip makes adjacent-result
// deltas attribute build, stringify, parse, and post-parse traversal costs;
// returning a value from every phase prevents the generated work from becoming
// dead code.  These are consumed by SharpTS.Microbenchmarks rather than the
// cross-runtime shell so the public cross-runtime workload remains unchanged.
export function jsonBuildPhase(n: number): number {
    const items: { id: number; name: string; value: number }[] = [];
    for (let i: number = 0; i < n; i++) {
        items.push({ id: i, name: "item-" + i, value: i * 3 - 1 });
    }
    return items.length + 0.5 - 0.5;
}

export function jsonStringifyPhase(n: number): number {
    const items: { id: number; name: string; value: number }[] = [];
    for (let i: number = 0; i < n; i++) {
        items.push({ id: i, name: "item-" + i, value: i * 3 - 1 });
    }
    const json: string = JSON.stringify({ items: items });
    return json.length + 0.5 - 0.5;
}

export function jsonParsePhase(n: number): number {
    const items: { id: number; name: string; value: number }[] = [];
    for (let i: number = 0; i < n; i++) {
        items.push({ id: i, name: "item-" + i, value: i * 3 - 1 });
    }
    const json: string = JSON.stringify({ items: items });
    const parsed = JSON.parse(json);
    const back: { id: number; name: string; value: number }[] = parsed.items;
    return back.length + 0.5 - 0.5;
}

// Typed-array numeric kernel: fill a Float64Array, then a 3-point stencil sweep.
// Data-parallel arithmetic over a real typed buffer — where compiled IL should
// approach native and the dynamic/boxed representation pays the most.
export function typedArrayKernel(n: number): number {
    const a = new Float64Array(n);
    for (let i: number = 0; i < n; i++) {
        a[i] = i * 1.5 + (i % 7);
    }
    let sum: number = 0;
    for (let i: number = 1; i < n - 1; i++) {
        sum = sum + (a[i - 1] - 2 * a[i] + a[i + 1]);
    }
    return sum;
}

// binary-trees (Computer Language Benchmarks Game): build a `{ left, right }`
// object tree to `depth`, then checksum it. Allocates and discards heavily —
// exercises the GC and recursion rather than arithmetic.
type TreeNode = { left: TreeNode | null; right: TreeNode | null };

function buildTree(depth: number): TreeNode {
    if (depth <= 0) {
        return { left: null, right: null };
    }
    return { left: buildTree(depth - 1), right: buildTree(depth - 1) };
}

function itemCheck(node: TreeNode | null): number {
    if (node === null) {
        return 1;
    }
    return 1 + itemCheck(node.left) + itemCheck(node.right);
}

export function binaryTrees(depth: number): number {
    return itemCheck(buildTree(depth));
}

// ── Async / Promise workloads ─────────────────────────────────────────────

// Await already-resolved promises serially. This isolates the cost of promise
// creation plus async suspension/resumption without I/O or timer latency.
export async function asyncSequentialAwait(n: number): Promise<number> {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + await Promise.resolve(i);
    }
    return sum;
}

async function asyncIdentity(value: number): Promise<number> {
    return value;
}

// Repeatedly invoke an async function whose body completes immediately. Unlike
// asyncSequentialAwait, this exercises generated async-function state machines
// without routing each value through Promise.resolve.
export async function asyncFunctionCalls(n: number): Promise<number> {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + await asyncIdentity(i);
    }
    return sum;
}

// Construct and settle a serial continuation chain. Capturing `i` on every
// iteration keeps closure and continuation allocation visible.
export async function promiseThenChain(n: number): Promise<number> {
    let chain: Promise<number> = Promise.resolve(0);
    for (let i: number = 0; i < n; i++) {
        chain = chain.then((sum: number): number => sum + i);
    }
    return await chain;
}

// Resolve a fan-out of already-settled promises and consume every result. This
// isolates Promise.all bookkeeping and result-array materialization.
export async function promiseAllFanOut(n: number): Promise<number> {
    const promises: Promise<number>[] = [];
    for (let i: number = 0; i < n; i++) {
        promises.push(Promise.resolve(i));
    }
    const values: number[] = await (
        Promise.all(promises) as Promise<number[]>
    );
    let sum: number = 0;
    for (let i: number = 0; i < values.length; i++) {
        sum = sum + values[i];
    }
    return sum;
}

// Promise.all phase probes used by the microbenchmark suite. The first keeps
// the exact input-construction loop but omits aggregation; the second reuses a
// single settled promise so Promise.all's per-element bookkeeping remains
// visible without paying for n Promise.resolve allocations.
export async function promiseResolveFanOutPhase(n: number): Promise<number> {
    const promises: Promise<number>[] = [];
    for (let i: number = 0; i < n; i++) {
        promises.push(Promise.resolve(i));
    }
    return promises.length;
}

export async function promiseAllRepeatedResolvedPhase(n: number): Promise<number> {
    const promise: Promise<number> = Promise.resolve(1);
    const promises: Promise<number>[] = [];
    for (let i: number = 0; i < n; i++) {
        promises.push(promise);
    }
    const values: number[] = await (
        Promise.all(promises) as Promise<number[]>
    );
    return values.length;
}

export async function promiseAllDistinctResolvedPhase(n: number): Promise<number> {
    const promises: Promise<number>[] = [];
    for (let i: number = 0; i < n; i++) {
        promises.push(Promise.resolve(i));
    }
    const values: number[] = await (
        Promise.all(promises) as Promise<number[]>
    );
    return values.length;
}

export function promiseAllResultConsumptionPhase(n: number): number {
    const values: number[] = [];
    for (let i: number = 0; i < n; i++) {
        values.push(i);
    }
    let sum: number = 0;
    for (let i: number = 0; i < values.length; i++) {
        sum = sum + values[i];
    }
    return sum;
}

export async function promiseRepeatedResolvedBuildPhase(n: number): Promise<number> {
    const promise: Promise<number> = Promise.resolve(1);
    const promises: Promise<number>[] = [];
    for (let i: number = 0; i < n; i++) {
        promises.push(promise);
    }
    return promises.length;
}
