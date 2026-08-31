import { MessageChannel, Worker } from "worker_threads";
import { benchAsync } from "./lib/bench.ts";

function runDirection(
    mode: string,
    messageCount: number,
    workerPath: string,
): Promise<any> {
    const channel: any = new MessageChannel();
    const worker: any = new Worker(workerPath, {
        workerData: { port: channel.port1, mode, count: messageCount },
        transferList: [channel.port1],
    });
    let resolveReady: any;
    let rejectReady: any;
    let resolveBurst: any = null;
    let rejectBurst: any = null;
    let received: number = 0;
    let checksum: number = 0;

    const ready: Promise<number> = new Promise((resolve: any, reject: any) => {
        resolveReady = resolve;
        rejectReady = reject;
    });

    worker.on("message", (message: any) => {
        if (message.kind === "ready") {
            resolveReady(1);
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

    channel.port2.on("message", (message: number) => {
        if (resolveBurst === null) {
            return;
        }

        if (mode === "emit") {
            received = received + 1;
            checksum = checksum + message;
            if (received < messageCount) {
                return;
            }
        } else {
            checksum = message;
        }

        const resolve: any = resolveBurst;
        resolveBurst = null;
        rejectBurst = null;
        resolve(checksum + messageCount);
    });

    function runBurst(): Promise<number> {
        received = 0;
        checksum = 0;
        return new Promise((resolve: any, reject: any) => {
            resolveBurst = resolve;
            rejectBurst = reject;
            if (mode === "emit") {
                channel.port2.postMessage(messageCount);
            } else {
                for (let i: number = 0; i < messageCount; i++) {
                    channel.port2.postMessage(i);
                }
            }
        });
    }

    return ready
        .then(() => runBurst())
        .then((result: number) => {
            const expected: number = messageCount * (messageCount - 1) / 2 + messageCount;
            if (result !== expected) {
                throw new Error(
                    "message-port burst checksum mismatch: expected " + expected +
                    ", got " + result,
                );
            }
            const name: string = mode === "emit"
                ? "worker-message-port-from-worker"
                : "worker-message-port-to-worker";
            return benchAsync(name, messageCount, runBurst);
        })
        .then(
            () => {
                channel.port2.close();
                return worker.terminate();
            },
            (error: any) => {
                channel.port2.close();
                return worker.terminate().then(() => { throw error; });
            },
        );
}

function runPortCases(messageCount: number, workerPath: string): Promise<any> {
    return runDirection("emit", messageCount, workerPath)
        .then(() => runDirection("receive", messageCount, workerPath));
}

function main(): Promise<any> {
    const moduleMeta: any = import.meta;
    const workerPath: string = moduleMeta.dirname + "/workers/message-port-burst-worker.ts";

    return runPortCases(1, workerPath)
        .then(() => runPortCases(100, workerPath))
        .then(() => runPortCases(1000, workerPath));
}

main().catch((error: any) => {
    console.error(error);
    process.exitCode = 1;
});
