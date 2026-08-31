import { parentPort } from "worker_threads";

if (parentPort === null) {
    throw new Error("worker-message-throughput worker requires parentPort");
}

parentPort.on("message", (message: any) => {
    if (message.kind !== "burst") {
        return;
    }

    for (let i: number = 0; i < message.count; i++) {
        parentPort!.postMessage({
            kind: "item",
            sequence: message.sequence,
            index: i,
        });
    }
});

parentPort.postMessage({ kind: "ready" });
