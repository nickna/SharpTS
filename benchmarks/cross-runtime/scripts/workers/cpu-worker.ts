import { parentPort } from "worker_threads";
import { cpuRangeChecksum } from "./cpu-kernel.ts";

if (parentPort === null) {
    throw new Error("worker-scaling CPU worker requires parentPort");
}

parentPort.on("message", (message: any) => {
    if (message.kind === "run") {
        const checksum: number = cpuRangeChecksum(message.start, message.end);
        parentPort!.postMessage({ kind: "result", checksum });
    }
});

parentPort.postMessage({ kind: "ready" });
