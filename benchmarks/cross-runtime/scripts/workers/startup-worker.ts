import { parentPort } from "worker_threads";

if (parentPort === null) {
    throw new Error("worker-lifecycle worker requires parentPort");
}

// Keep the worker alive until the parent explicitly terminates it, so every measured
// invocation has the same create -> ready -> terminate lifecycle in every runtime.
parentPort.on("message", () => {});
parentPort.postMessage({ kind: "ready" });
