import { Worker } from "worker_threads";
import { bench, benchAsync } from "./lib/bench.ts";
import { combineChecksums, cpuRangeChecksum } from "./workers/cpu-kernel.ts";

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
                return;
            }

            if (message.kind === "result") {
                const resolveResult: any = resultResolvers[i];
                resultResolvers[i] = null;
                resultRejecters[i] = null;
                if (resolveResult !== null) {
                    resolveResult(message.checksum);
                }
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
                const job: Promise<number> = new Promise((resolve: any, reject: any) => {
                    resultResolvers[i] = resolve;
                    resultRejecters[i] = reject;
                    workers[i].postMessage({ kind: "run", start, end });
                });
                jobs.push(job);
                start = end;
            }

            return Promise.all(jobs).then((checksums: any) => combineChecksums(checksums));
        },
        close: (): Promise<number> => {
            const exits: Promise<number>[] = [];
            for (let i: number = 0; i < workers.length; i++) {
                exits.push(workers[i].terminate());
            }
            return Promise.all(exits).then((exitCodes: any) => combineChecksums(exitCodes));
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
                    "worker checksum mismatch for " + workerCount +
                    " workers: expected " + expected + ", got " + actual,
                );
            }
            return benchAsync(
                "worker-cpu-fixed-work",
                workerCount,
                () => pool.run(totalItems),
            );
        })
        .then(
            () => pool.close(),
            (error: any) => pool.close().then(() => { throw error; }),
        );
}

function main(): Promise<any> {
    const totalItems: number = 200000;
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/cpu-worker.ts";
    const expected: number = cpuRangeChecksum(0, totalItems);
    bench("worker-cpu-direct", totalItems, () => cpuRangeChecksum(0, totalItems));

    return runWorkerCase(1, workerPath, totalItems, expected)
        .then(() => runWorkerCase(2, workerPath, totalItems, expected))
        .then(() => runWorkerCase(4, workerPath, totalItems, expected));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
