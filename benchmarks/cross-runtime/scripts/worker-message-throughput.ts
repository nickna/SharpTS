import { Worker } from "worker_threads";
import { benchAsync } from "./lib/bench.ts";

function runBurstCase(messageCount: number, workerPath: string): Promise<any> {
    const worker: any = new Worker(workerPath);
    let sequence: number = 0;
    let received: number = 0;
    let checksum: number = 0;
    let resolveReady: any;
    let rejectReady: any;
    let resolveBurst: any = null;
    let rejectBurst: any = null;

    const ready: Promise<number> = new Promise((resolve: any, reject: any) => {
        resolveReady = resolve;
        rejectReady = reject;
    });

    worker.on("message", (message: any) => {
        if (message.kind === "ready") {
            resolveReady(1);
            return;
        }

        if (message.kind !== "item" || resolveBurst === null) {
            return;
        }

        if (message.sequence !== sequence) {
            const reject: any = rejectBurst;
            resolveBurst = null;
            rejectBurst = null;
            reject(new Error(
                "worker burst sequence mismatch: expected " + sequence +
                ", got " + message.sequence,
            ));
            return;
        }

        received = received + 1;
        checksum = checksum + message.index;
        if (received === messageCount) {
            const resolve: any = resolveBurst;
            resolveBurst = null;
            rejectBurst = null;
            resolve(checksum + received);
        }
    });

    worker.on("error", (error: any) => {
        rejectReady(error);
        if (rejectBurst !== null) {
            const reject: any = rejectBurst;
            resolveBurst = null;
            rejectBurst = null;
            reject(error);
        }
    });

    function runBurst(): Promise<number> {
        sequence = sequence + 1;
        received = 0;
        checksum = 0;
        return new Promise((resolve: any, reject: any) => {
            resolveBurst = resolve;
            rejectBurst = reject;
            worker.postMessage({ kind: "burst", sequence, count: messageCount });
        });
    }

    return ready
        .then(() => runBurst())
        .then((result: number) => {
            const expected: number = messageCount * (messageCount - 1) / 2 + messageCount;
            if (result !== expected) {
                throw new Error(
                    "worker burst checksum mismatch: expected " + expected +
                    ", got " + result,
                );
            }
            return benchAsync(
                "worker-message-burst",
                messageCount,
                runBurst,
            );
        })
        .then(
            () => worker.terminate(),
            (error: any) => worker.terminate().then(() => { throw error; }),
        );
}

function main(): Promise<any> {
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/burst-worker.ts";

    return runBurstCase(1, workerPath)
        .then(() => runBurstCase(100, workerPath))
        .then(() => runBurstCase(1000, workerPath));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
