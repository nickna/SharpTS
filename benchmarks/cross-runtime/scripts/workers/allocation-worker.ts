import { parentPort } from "worker_threads";
import { allocationChecksum } from "./allocation-kernel.ts";

if (parentPort === null) {
    throw new Error("worker-allocation-scaling worker requires parentPort");
}

parentPort.on("message", (message: any) => {
    if (message.kind === "run") {
        parentPort!.postMessage({
            kind: "result",
            checksum: allocationChecksum(message.start, message.end),
        });
    }
});

parentPort.postMessage({ kind: "ready" });
