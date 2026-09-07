// node --experimental-strip-types --experimental-transform-types --no-warnings benchmarks/cross-runtime/test-worker-allocation.mjs
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { allocationChecksum } from './scripts/workers/allocation-kernel.ts';
import { expectedAllocationChecksum } from './scripts/workers/allocation-expected.ts';
import { createWorkerPool } from './scripts/lib/worker-pool.ts';

for (const [start, end] of [[0, 0], [0, 1], [5, 17], [99, 101], [103, 8193], [0, 20000]]) {
    assert.equal(allocationChecksum(start, end), expectedAllocationChecksum(start, end));
}
assert.equal(expectedAllocationChecksum(0, 20000), 800178000);
const pool = createWorkerPool(4, fileURLToPath(new URL('./scripts/workers/allocation-worker.ts', import.meta.url)));
try {
    await pool.ready;
    for (const total of [1, 7, 20003]) assert.equal(await pool.run(total), expectedAllocationChecksum(0, total));
} finally {
    await pool.close();
}

function run(script, overrides = {}) {
    const env = { ...process.env };
    for (const key of Object.keys(env)) if (key.startsWith('SHARPTS_BENCH_')) delete env[key];
    Object.assign(env, { SHARPTS_BENCH_WARMUP_MS: '0', SHARPTS_BENCH_SAMPLE_MS: '2' }, overrides);
    return spawnSync(process.execPath, [
        '--experimental-strip-types', '--experimental-transform-types', '--no-warnings',
        fileURLToPath(new URL(`./scripts/${script}.ts`, import.meta.url)),
    ], { env, encoding: 'utf8', timeout: 30000 });
}

for (const [script, count] of [['worker-allocation-scaling', 2], ['worker-allocation-boundaries', 6]]) {
    const listed = run(script, { SHARPTS_BENCH_LIST_CASES: '1' });
    assert.equal(listed.status, 0, listed.error?.message ?? listed.stderr);
    const lines = listed.stdout.trim().split(/\r?\n/);
    assert.equal(lines.length, count);
    assert.equal(new Set(lines).size, count);
    assert.ok(lines.every(line => line.startsWith('BENCH_CASE:')));
}

const selected = run('worker-allocation-scaling', {
    SHARPTS_BENCH_CASE: 'worker-allocation-fixed-work', SHARPTS_BENCH_PARAM: '2',
});
assert.equal(selected.status, 0, selected.stderr);
assert.match(selected.stdout, /^BENCH:worker-allocation-fixed-work:2:/);
assert.equal(selected.stdout.trim().split(/\r?\n/).length, 1);
const omitted = run('worker-allocation-scaling', { SHARPTS_BENCH_CASE: 'absent' });
assert.equal(omitted.status, 0, omitted.stderr);
assert.equal(omitted.stdout, '');

for (const [name, values] of [
    ['SHARPTS_BENCH_WARMUP_MS', ['-1', 'NaN', '60001', ' ']],
    ['SHARPTS_BENCH_SAMPLE_MS', ['0', 'Infinity', '-1', '60001', ' ']],
]) {
    for (const value of values) {
        const invalid = run('worker-allocation-scaling', { [name]: value });
        assert.notEqual(invalid.status, 0);
        assert.ok(invalid.stderr.includes(name), invalid.stderr);
    }
}
console.log('Worker allocation harness checks passed.');
