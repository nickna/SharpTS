// Benchmark — perf_hooks demo comparing algorithmic costs
// Usage: sharpts samples/benchmark.ts          (interpreted)
//        sharpts --compile samples/benchmark.ts -o out.dll  (compiled)
//        dotnet out.dll
//
// Demonstrates: perf_hooks.performance.now(), performance.mark/measure,
//               PerformanceObserver, and a rough interpreted-vs-compiled comparison.
//
// Tip: run both modes and compare the "total" time. Compiled mode typically
// runs a hot numeric loop 20–50x faster than the tree-walking interpreter.

import { performance, PerformanceObserver } from 'perf_hooks';

interface Result {
    name: string;
    ms: number;
    ops: number;
    opsPerSec: number;
}

function bench(name: string, iterations: number, body: () => void): Result {
    // Warm-up pass — first call through a function is often slower than steady state.
    body();
    const start = performance.now();
    for (let i = 0; i < iterations; i = i + 1) {
        body();
    }
    const ms = performance.now() - start;
    return {
        name: name,
        ms: ms,
        ops: iterations,
        opsPerSec: iterations / (ms / 1000),
    };
}

function fmt(n: number, digits: number): string {
    if (n >= 1000000) return (n / 1000000).toFixed(digits) + 'M';
    if (n >= 1000) return (n / 1000).toFixed(digits) + 'K';
    return n.toFixed(digits);
}

function printResult(r: Result): void {
    const msStr = r.ms.toFixed(2) + ' ms';
    const opsStr = fmt(r.opsPerSec, 1) + ' ops/sec';
    console.log('  ' + r.name.padEnd(30) + msStr.padStart(12) + '   ' + opsStr.padStart(16));
}

// Workloads — small, CPU-bound, no allocation in the hot path.

function sumLoop(): number {
    let s = 0;
    for (let i = 0; i < 10000; i = i + 1) s = s + i;
    return s;
}

function fib(n: number): number {
    if (n < 2) return n;
    return fib(n - 1) + fib(n - 2);
}

function stringConcat(): string {
    let s = '';
    for (let i = 0; i < 100; i = i + 1) s = s + 'x';
    return s;
}

function arrayBuild(): number {
    const a: number[] = [];
    for (let i = 0; i < 1000; i = i + 1) a.push(i * 2);
    let t = 0;
    for (let i = 0; i < a.length; i = i + 1) t = t + a[i];
    return t;
}

// ─── Main ─────────────────────────────────────────────────────────────────

console.log('SharpTS Benchmark (perf_hooks)');
console.log('==============================');
console.log('');
console.log('  ' + 'workload'.padEnd(30) + 'time'.padStart(12) + '   ' + 'throughput'.padStart(16));
console.log('  ' + '-'.repeat(30) + '-'.repeat(12) + '   ' + '-'.repeat(16));

const results: Result[] = [];
results.push(bench('sum 0..9999 (int add)',   200, () => { sumLoop(); }));
results.push(bench('fib(20) recursion',        20, () => { fib(20); }));
results.push(bench('100-char string concat', 500, () => { stringConcat(); }));
results.push(bench('build + sum 1000 ints',  200, () => { arrayBuild(); }));

for (const r of results) printResult(r);

console.log('');

// ─── Named phases via performance.mark() / measure() + PerformanceObserver ─

console.log('Named phases via performance.mark() / measure():');
console.log('------------------------------------------------');

// Subscribe synchronously — the observer's callback fires from within
// performance.measure() as each entry is recorded.
const obs = new PerformanceObserver((list: any) => {
    for (const m of list.getEntries()) {
        console.log('  measured: ' + m.name + ' = ' + m.duration.toFixed(2) + ' ms');
    }
});
obs.observe({ entryTypes: ['measure'] });

performance.mark('load-start');
for (let i = 0; i < 50; i = i + 1) sumLoop();
performance.mark('load-end');
performance.measure('cold-load', 'load-start', 'load-end');

performance.mark('warm-start');
for (let i = 0; i < 50; i = i + 1) sumLoop();
performance.mark('warm-end');
performance.measure('warm-load', 'warm-start', 'warm-end');

obs.disconnect();

console.log('');

// ─── Summary ─────────────────────────────────────────────────────────────

let total = 0;
for (const r of results) total = total + r.ms;
console.log('Total benchmark time: ' + total.toFixed(2) + ' ms');
console.log('timeOrigin (unix ms): ' + performance.timeOrigin.toFixed(0));
console.log('');
console.log('Run this file in both modes to compare:');
console.log('  sharpts samples/benchmark.ts                          (interpreted)');
console.log('  sharpts --compile samples/benchmark.ts -o b.dll && \\');
console.log('  dotnet b.dll                                           (compiled)');
