import { createWorkerPool } from "./lib/worker-pool.ts";
import { bench, benchAsync, listCases, shouldRunCase } from "./lib/bench.ts";
import { allocationChecksum } from "./workers/allocation-kernel.ts";


function runWorkerCase(
    workerCount: number,
    workerPath: string,
    totalItems: number,
    expected: number,
): Promise<any> {
    if (!shouldRunCase("worker-allocation-fixed-work", workerCount)) {
        return Promise.resolve(0);
    }
    const pool: any = createWorkerPool(workerCount, workerPath);
    return pool.ready
        .then(() => pool.run(totalItems))
        .then((actual: number) => {
            if (actual !== expected) {
                throw new Error(
                    "worker allocation checksum mismatch: expected " + expected +
                    ", got " + actual,
                );
            }
            return benchAsync(
                "worker-allocation-fixed-work",
                workerCount,
                () => pool.run(totalItems),
                expected,
            );
        })
        .then(
            () => pool.close(),
            (error: any) => pool.close().then(() => { throw error; }),
        );
}

function main(): Promise<any> {
    if (listCases) {
        console.log("BENCH_CASE:worker-allocation-direct");
        console.log("BENCH_CASE:worker-allocation-fixed-work");
        return Promise.resolve(0);
    }
    const totalItems: number = 20000;
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/allocation-worker.ts";
    const expected: number = 800178000;

    bench("worker-allocation-direct", totalItems, () => allocationChecksum(0, totalItems), expected);
    return runWorkerCase(1, workerPath, totalItems, expected)
        .then(() => runWorkerCase(2, workerPath, totalItems, expected))
        .then(() => runWorkerCase(4, workerPath, totalItems, expected));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
