import { Worker } from "worker_threads";
import { bench, benchAsync } from "./lib/bench.ts";
import { allocationChecksum } from "./workers/allocation-kernel.ts";

function createWorkerPool(workerCount: number, workerPath: string): any {
    const workers: any[] = [];
    const resultResolvers: any[] = [];
    const resultRejecters: any[] = [];
    let readyCount: number = 0;
    let resolveReady: any;
    let rejectReady: any;

    const ready: Promise<number> = new Promise((resolve: any, reject: any) => {
        resolveReady = resolve;
        rejectReady = reject;
    });

    for (let i: number = 0; i < workerCount; i++) {
        const worker: any = new Worker(workerPath);
        workers.push(worker);
        resultResolvers.push(null);
        resultRejecters.push(null);

        worker.on("message", (message: any) => {
            if (message.kind === "ready") {
                readyCount = readyCount + 1;
                if (readyCount === workerCount) {
                    resolveReady(readyCount);
                }
            } else if (message.kind === "result") {
                const resolveResult: any = resultResolvers[i];
                resultResolvers[i] = null;
                resultRejecters[i] = null;
                resolveResult(message.checksum);
            }
        });

        worker.on("error", (error: any) => {
            rejectReady(error);
            const rejectResult: any = resultRejecters[i];
            resultResolvers[i] = null;
            resultRejecters[i] = null;
            if (rejectResult !== null) {
                rejectResult(error);
            }
        });
    }

    return {
        ready,
        run: (totalItems: number): Promise<number> => {
            const jobs: Promise<number>[] = [];
            const baseSize: number = Math.floor(totalItems / workerCount);
            const remainder: number = totalItems % workerCount;
            let start: number = 0;

            for (let i: number = 0; i < workerCount; i++) {
                const size: number = baseSize + (i < remainder ? 1 : 0);
                const end: number = start + size;
                jobs.push(new Promise((resolve: any, reject: any) => {
                    resultResolvers[i] = resolve;
                    resultRejecters[i] = reject;
                    workers[i].postMessage({ kind: "run", start, end });
                }));
                start = end;
            }

            return Promise.all(jobs).then((checksums: any) => {
                let checksum: number = 0;
                for (let i: number = 0; i < checksums.length; i++) {
                    checksum = checksum + checksums[i];
                }
                return checksum;
            });
        },
        close: (): Promise<number> => {
            const exits: Promise<number>[] = [];
            for (let i: number = 0; i < workers.length; i++) {
                exits.push(workers[i].terminate());
            }
            return Promise.all(exits).then((codes: any) => codes.length);
        },
    };
}

function runWorkerCase(
    workerCount: number,
    workerPath: string,
    totalItems: number,
    expected: number,
): Promise<any> {
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
    const totalItems: number = 20000;
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/allocation-worker.ts";
    // Sum 4*i + 4 + (i % 100 < 10 ? 6 : 7), for 0 <= i < 20000.
    // Keep the oracle independent of the kernel under test.
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
