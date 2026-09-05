// Shared cross-runtime micro-benchmark harness.
//
// Runs on the SharpTS interpreter, the SharpTS compiler, Node.js, and Bun, so
// the timing methodology is identical everywhere. Uses `performance.now()`
// (sub-microsecond, monotonic) instead of `Date.now()` (~1ms granular) so that
// fast cases are no longer quantized to zero.
//
// Each call is measured by auto-batching: the harness grows an inner repeat
// count until one timed sample spans >= 1ms (lifting it above the timer's
// noise floor), then collects samples until a time budget elapses and reports
// the per-call mean, min, and sample standard deviation.
//
// Cases may provide an expected numeric result. Validation runs before and
// after sampling, outside the timed region, so optimized-code miscompilations
// fail the workload without charging correctness checks to the measurement.
//
// Output line (consumed by the cross-runtime PowerShell tools):
//   BENCH:<name>:<param>:<meanMs>:<minMs>:<stdevMs>:<samples>:<inner>:<sampledMs>

import { performance } from "perf_hooks";

const configuredWarmup = process.env.SHARPTS_BENCH_WARMUP_MS;
const WARMUP_CAP_MS: number = configuredWarmup === undefined ? 100 : Number(configuredWarmup);
if (configuredWarmup !== undefined && (configuredWarmup.trim() === "" ||
    !Number.isInteger(WARMUP_CAP_MS) || WARMUP_CAP_MS < 0 || WARMUP_CAP_MS > 10000)) {
    console.error("SHARPTS_BENCH_WARMUP_MS must be an integer from 0 to 10000");
    process.exit(1);
}
const SLOW_CALL_MS: number = 100;   // sampling policy is independent of warmup duration
const MIN_SAMPLE_MS: number = 1;     // grow the inner batch until a sample spans this
const BUDGET_MS: number = 300;       // preferred total sampling time per case
const MIN_SAMPLES: number = 8;       // sample floor (for a meaningful stdev)...
const HARD_CAP_MS: number = 2000;    // ...but never exceed this, even below the floor
const MAX_SAMPLES: number = 100000;
const MAX_INNER: number = 1 << 24;
const OUTPUT_SCALE: number = 10000000; // seven decimal places in milliseconds (0.1 ns)
const requestedCase = process.env.SHARPTS_BENCH_CASE;
const listCases: boolean = process.env.SHARPTS_BENCH_LIST_CASES === "1";

function round(x: number): number {
    return Math.round(x * OUTPUT_SCALE) / OUTPUT_SCALE;
}

function validateExpected(name: string, param: number, actual: number, expected?: number): void {
    if (expected !== undefined && actual !== expected) {
        throw new Error(
            "Benchmark checksum mismatch for " + name + "(" + param + "): expected " +
            expected + ", received " + actual,
        );
    }
}

// `fn` returns a number that is accumulated into a guard so neither the
// interpreter nor the compiler can dead-code-eliminate the measured work.
export function bench(name: string, param: number, fn: () => number, expected?: number): void {
    if (listCases) {
        console.log("BENCH_CASE:" + name);
        return;
    }
    if (requestedCase && requestedCase !== name) {
        return;
    }
    let guard: number = 0;
    const samples: number[] = [];
    let total: number = 0;
    let inner: number = 1;

    if (expected !== undefined) {
        const validationResult: number = fn();
        validateExpected(name, param, validationResult, expected);
        guard = guard + validationResult;
    }

    // Discard a cold probe, then give every runtime the same time-bounded warmup.
    // Previously a first call over WARMUP_CAP_MS was retained as sample zero and
    // skipped warmup entirely. That made slow interpreter cases measure startup
    // while fast JIT cases measured steady state.
    guard = guard + fn();
    if (WARMUP_CAP_MS > 0) {
        const warmStart: number = performance.now();
        do {
            guard = guard + fn();
        } while (performance.now() - warmStart < WARMUP_CAP_MS);
    }

    // This post-warmup probe selects single-call sampling versus auto-batching,
    // but is itself discarded so both branches start with fresh observations.
    const probeStart: number = performance.now();
    guard = guard + fn();
    const firstMs: number = performance.now() - probeStart;

    if (firstMs >= SLOW_CALL_MS) {
        // A single call is reliably measurable — sample one call at a time,
        // bounded by the budget and the hard cap (slow cases end up with few
        // samples, and thus stdev 0, which is honest).
        while (samples.length < MAX_SAMPLES) {
            if (total >= HARD_CAP_MS) {
                break;
            }
            if (total >= BUDGET_MS && samples.length >= MIN_SAMPLES) {
                break;
            }
            const t0: number = performance.now();
            guard = guard + fn();
            const elapsed: number = performance.now() - t0;
            samples.push(elapsed);
            total = total + elapsed;
        }
    } else {
        // Fast call: calibrate an inner batch so a sample spans >= MIN_SAMPLE_MS,
        // then collect budgeted samples.
        while (inner < MAX_INNER) {
            const c0: number = performance.now();
            for (let k: number = 0; k < inner; k++) {
                guard = guard + fn();
            }
            const dc: number = performance.now() - c0;
            if (dc >= MIN_SAMPLE_MS) {
                break;
            }
            inner = inner * 2;
        }

        while (samples.length < MAX_SAMPLES) {
            const t0: number = performance.now();
            for (let k: number = 0; k < inner; k++) {
                guard = guard + fn();
            }
            const elapsed: number = performance.now() - t0;
            samples.push(elapsed / inner);
            total = total + elapsed;

            if (total >= HARD_CAP_MS) {
                break;
            }
            if (total >= BUDGET_MS && samples.length >= MIN_SAMPLES) {
                break;
            }
        }
    }

    // Mean / min / sample standard deviation over the per-call samples.
    let sum: number = 0;
    let min: number = samples[0];
    for (let i: number = 0; i < samples.length; i++) {
        sum = sum + samples[i];
        if (samples[i] < min) {
            min = samples[i];
        }
    }
    const mean: number = sum / samples.length;

    let varSum: number = 0;
    for (let i: number = 0; i < samples.length; i++) {
        const d: number = samples[i] - mean;
        varSum = varSum + d * d;
    }
    const stdev: number = samples.length > 1 ? Math.sqrt(varSum / (samples.length - 1)) : 0;

    // Re-check after the workload has reached optimized steady state. This is
    // deliberately outside every measured sample.
    if (expected !== undefined) {
        const validationResult: number = fn();
        validateExpected(name, param, validationResult, expected);
        guard = guard + validationResult;
    }

    // Anti-dead-code-elimination: force `guard` to be observably used.
    if (guard === -1) {
        console.log("guard:" + guard);
    }

    console.log(
        "BENCH:" + name + ":" + param + ":" + round(mean) + ":" + round(min) + ":" +
        round(stdev) + ":" + samples.length + ":" + inner + ":" + round(total),
    );
}

// Async counterpart to `bench`. Each invocation is awaited inside the timed
// region so the result includes promise creation, continuation scheduling, and
// settlement rather than only the synchronous prefix before the first await.
// Fast async functions are auto-batched with sequential awaits; this preserves
// deterministic checksums and avoids introducing Promise.all into every probe.
export async function benchAsync(
    name: string,
    param: number,
    fn: () => Promise<number>,
    expected?: number,
): Promise<void> {
    if (listCases) {
        console.log("BENCH_CASE:" + name);
        return;
    }
    if (requestedCase && requestedCase !== name) {
        return;
    }

    let guard: number = 0;
    const samples: number[] = [];
    let total: number = 0;
    let inner: number = 1;

    if (expected !== undefined) {
        const validationResult: number = await fn();
        validateExpected(name, param, validationResult, expected);
        guard = guard + validationResult;
    }

    // Keep the async methodology identical to the synchronous path: discard the
    // cold call, warm every runtime, and discard the post-warmup routing probe.
    guard = guard + await fn();
    if (WARMUP_CAP_MS > 0) {
        const warmStart: number = performance.now();
        do {
            guard = guard + await fn();
        } while (performance.now() - warmStart < WARMUP_CAP_MS);
    }

    const probeStart: number = performance.now();
    guard = guard + await fn();
    const firstMs: number = performance.now() - probeStart;

    if (firstMs >= SLOW_CALL_MS) {
        while (samples.length < MAX_SAMPLES) {
            if (total >= HARD_CAP_MS) {
                break;
            }
            if (total >= BUDGET_MS && samples.length >= MIN_SAMPLES) {
                break;
            }
            const t0: number = performance.now();
            guard = guard + await fn();
            const elapsed: number = performance.now() - t0;
            samples.push(elapsed);
            total = total + elapsed;
        }
    } else {
        while (inner < MAX_INNER) {
            const c0: number = performance.now();
            for (let k: number = 0; k < inner; k++) {
                guard = guard + await fn();
            }
            const dc: number = performance.now() - c0;
            if (dc >= MIN_SAMPLE_MS) {
                break;
            }
            inner = inner * 2;
        }

        while (samples.length < MAX_SAMPLES) {
            const t0: number = performance.now();
            for (let k: number = 0; k < inner; k++) {
                guard = guard + await fn();
            }
            const elapsed: number = performance.now() - t0;
            samples.push(elapsed / inner);
            total = total + elapsed;

            if (total >= HARD_CAP_MS) {
                break;
            }
            if (total >= BUDGET_MS && samples.length >= MIN_SAMPLES) {
                break;
            }
        }
    }

    let sum: number = 0;
    let min: number = samples[0];
    for (let i: number = 0; i < samples.length; i++) {
        sum = sum + samples[i];
        if (samples[i] < min) {
            min = samples[i];
        }
    }
    const mean: number = sum / samples.length;

    let varSum: number = 0;
    for (let i: number = 0; i < samples.length; i++) {
        const d: number = samples[i] - mean;
        varSum = varSum + d * d;
    }
    const stdev: number = samples.length > 1 ? Math.sqrt(varSum / (samples.length - 1)) : 0;

    if (expected !== undefined) {
        const validationResult: number = await fn();
        validateExpected(name, param, validationResult, expected);
        guard = guard + validationResult;
    }

    if (guard === -1) {
        console.log("guard:" + guard);
    }

    console.log(
        "BENCH:" + name + ":" + param + ":" + round(mean) + ":" + round(min) + ":" +
        round(stdev) + ":" + samples.length + ":" + inner + ":" + round(total),
    );
}
