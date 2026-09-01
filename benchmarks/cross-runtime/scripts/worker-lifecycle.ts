import { Worker } from "worker_threads";
import { benchAsync } from "./lib/bench.ts";

function runLifecycle(workerCount: number, workerPath: string): Promise<number> {
    const workers: any[] = [];
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
        worker.on("message", (message: any) => {
            if (message.kind === "ready") {
                readyCount = readyCount + 1;
                if (readyCount === workerCount) {
                    resolveReady(readyCount);
                }
            }
        });
        worker.on("error", (error: any) => rejectReady(error));
    }

    return ready.then(() => {
        const exits: Promise<number>[] = [];
        for (let i: number = 0; i < workers.length; i++) {
            exits.push(workers[i].terminate());
        }
        return Promise.all(exits).then((codes: any) => {
            let checksum: number = readyCount;
            for (let i: number = 0; i < codes.length; i++) {
                checksum = checksum + codes[i];
            }
            return checksum;
        });
    });
}

function main(): Promise<any> {
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/startup-worker.ts";

    return benchAsync("worker-lifecycle", 1, () => runLifecycle(1, workerPath))
        .then(() => benchAsync("worker-lifecycle", 2, () => runLifecycle(2, workerPath)))
        .then(() => benchAsync("worker-lifecycle", 4, () => runLifecycle(4, workerPath)));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
