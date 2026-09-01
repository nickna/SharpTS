import { Worker } from "worker_threads";
import { benchAsync } from "./lib/bench.ts";

const STRIDE: number = 16; // 64 bytes between disjoint Int32 counters.

function createWorkerPool(workerCount: number, workerPath: string): any {
    const sharedBuffer: SharedArrayBuffer = new SharedArrayBuffer(workerCount * STRIDE * 4);
    const counters = new Int32Array(sharedBuffer);
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
        const worker: any = new Worker(workerPath, { workerData: sharedBuffer });
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

            if (message.kind === "done") {
                const resolveResult: any = resultResolvers[i];
                resultResolvers[i] = null;
                resultRejecters[i] = null;
                if (resolveResult !== null) {
                    resolveResult(message.iterations);
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
        run: (totalIterations: number, contended: boolean): Promise<number> => {
            const replies: Promise<number>[] = [];
            const baseSize: number = Math.floor(totalIterations / workerCount);
            const remainder: number = totalIterations % workerCount;

            for (let i: number = 0; i < counters.length; i++) {
                Atomics.store(counters, i, 0);
            }

            for (let i: number = 0; i < workerCount; i++) {
                const iterations: number = baseSize + (i < remainder ? 1 : 0);
                const index: number = contended ? 0 : i * STRIDE;
                const reply: Promise<number> = new Promise((resolve: any, reject: any) => {
                    resultResolvers[i] = resolve;
                    resultRejecters[i] = reject;
                    workers[i].postMessage({ kind: "run", index, iterations });
                });
                replies.push(reply);
            }

            return Promise.all(replies).then(() => {
                let actual: number = 0;
                if (contended) {
                    actual = Atomics.load(counters, 0);
                } else {
                    for (let i: number = 0; i < workerCount; i++) {
                        actual = actual + Atomics.load(counters, i * STRIDE);
                    }
                }
                if (actual !== totalIterations) {
                    throw new Error(
                        "atomic counter mismatch for " + workerCount + " workers: expected " +
                        totalIterations + ", got " + actual,
                    );
                }
                return actual;
            });
        },
        close: (): Promise<number> => {
            const exits: Promise<number>[] = [];
            for (let i: number = 0; i < workers.length; i++) {
                exits.push(workers[i].terminate());
            }
            return Promise.all(exits).then((exitCodes: any) => {
                let checksum: number = 0;
                for (let i: number = 0; i < exitCodes.length; i++) {
                    checksum = checksum + exitCodes[i];
                }
                return checksum;
            });
        },
    };
}

function runWorkerCase(workerCount: number, workerPath: string): Promise<any> {
    const totalIterations: number = 100000;
    const pool: any = createWorkerPool(workerCount, workerPath);
    return pool.ready
        .then(() => pool.run(totalIterations, false))
        .then(() => benchAsync(
            "worker-atomics-disjoint",
            workerCount,
            () => pool.run(totalIterations, false),
        ))
        .then(() => pool.run(totalIterations, true))
        .then(() => benchAsync(
            "worker-atomics-contended",
            workerCount,
            () => pool.run(totalIterations, true),
        ))
        .then(
            () => pool.close(),
            (error: any) => pool.close().then(() => { throw error; }),
        );
}

function main(): Promise<any> {
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/atomics-worker.ts";

    return runWorkerCase(1, workerPath)
        .then(() => runWorkerCase(2, workerPath))
        .then(() => runWorkerCase(4, workerPath));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
