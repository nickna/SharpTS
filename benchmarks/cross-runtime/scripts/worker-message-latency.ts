import { Worker } from "worker_threads";
import { benchAsync } from "./lib/bench.ts";

function createWorkerPool(workerCount: number, workerPath: string): any {
    const workers: any[] = [];
    const resultResolvers: any[] = [];
    const resultRejecters: any[] = [];
    let readyCount: number = 0;
    let nextSequence: number = 0;
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

            if (message.kind === "pong") {
                const resolveResult: any = resultResolvers[i];
                const rejectResult: any = resultRejecters[i];
                resultResolvers[i] = null;
                resultRejecters[i] = null;
                if (message.sequence !== nextSequence) {
                    if (rejectResult !== null) {
                        rejectResult(new Error(
                            "worker message sequence mismatch: expected " + nextSequence +
                            ", got " + message.sequence,
                        ));
                    }
                } else if (resolveResult !== null) {
                    resolveResult(message.sequence + i);
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
        roundTrip: (): Promise<number> => {
            nextSequence = nextSequence + 1;
            const sequence: number = nextSequence;
            const replies: Promise<number>[] = [];

            for (let i: number = 0; i < workerCount; i++) {
                const reply: Promise<number> = new Promise((resolve: any, reject: any) => {
                    resultResolvers[i] = resolve;
                    resultRejecters[i] = reject;
                    workers[i].postMessage({ kind: "ping", sequence });
                });
                replies.push(reply);
            }

            return Promise.all(replies).then((values: any) => {
                let checksum: number = 0;
                for (let i: number = 0; i < values.length; i++) {
                    checksum = checksum + values[i];
                }
                return checksum;
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
    const pool: any = createWorkerPool(workerCount, workerPath);
    return pool.ready
        .then(() => pool.roundTrip())
        .then((checksum: number) => {
            if (checksum <= 0) {
                throw new Error("worker message round-trip produced an invalid checksum");
            }
            return benchAsync(
                "worker-message-roundtrip",
                workerCount,
                () => pool.roundTrip(),
            );
        })
        .then(
            () => pool.close(),
            (error: any) => pool.close().then(() => { throw error; }),
        );
}

function main(): Promise<any> {
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/message-worker.ts";

    return runWorkerCase(1, workerPath)
        .then(() => runWorkerCase(2, workerPath))
        .then(() => runWorkerCase(4, workerPath));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
