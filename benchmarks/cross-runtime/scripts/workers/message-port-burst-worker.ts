import { parentPort, workerData } from "worker_threads";

if (parentPort === null) {
    throw new Error("message-port burst worker requires parentPort");
}

const port: any = workerData.port;
const messageCount: number = workerData.count;

if (workerData.mode === "emit") {
    port.on("message", () => {
        for (let i: number = 0; i < messageCount; i++) {
            port.postMessage(i);
        }
    });
} else {
    let received: number = 0;
    let checksum: number = 0;
    port.on("message", (message: number) => {
        received = received + 1;
        checksum = checksum + message;
        if (received === messageCount) {
            port.postMessage(checksum);
            received = 0;
            checksum = 0;
        }
    });
}

parentPort.postMessage({ kind: "ready" });
