import { parentPort, workerData } from "worker_threads";

if (parentPort === null) {
    throw new Error("worker-atomics-scaling worker requires parentPort");
}

function run(message: any): void {
    if (message.kind === "run") {
        const counters = new Int32Array(workerData);
        const iterations: number = message.iterations;
        const index: number = message.index;
        for (let i: number = 0; i < iterations; i++) {
            Atomics.add(counters, index, 1);
        }
        parentPort!.postMessage({ kind: "done", iterations });
    }
}

parentPort.on("message", run);

parentPort.postMessage({ kind: "ready" });
