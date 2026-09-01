import { parentPort } from "worker_threads";

if (parentPort === null) {
    throw new Error("worker-message-latency worker requires parentPort");
}

parentPort.on("message", (message: any) => {
    if (message.kind === "ping") {
        parentPort!.postMessage({ kind: "pong", sequence: message.sequence });
    }
});

parentPort.postMessage({ kind: "ready" });
