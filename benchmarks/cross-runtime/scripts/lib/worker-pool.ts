import { Worker } from "worker_threads";

export function createWorkerPool(workerCount: number, workerPath: string): any {
    const workers: any[] = [];
    const resultResolvers: any[] = [];
    const resultRejecters: any[] = [];
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
        resultResolvers.push(null);
        resultRejecters.push(null);

        worker.on("message", (message: any) => {
            if (message.kind === "ready") {
                readyCount = readyCount + 1;
                if (readyCount === workerCount) {
                    resolveReady(readyCount);
                }
            } else if (message.kind === "result") {
                const resolveResult: any = resultResolvers[i];
                resultResolvers[i] = null;
                resultRejecters[i] = null;
                resolveResult(message.checksum);
            }
        });

        worker.on("error", (error: any) => {
            rejectReady(error);
            const rejectResult: any = resultRejecters[i];
            resultResolvers[i] = null;
            resultRejecters[i] = null;
            if (rejectResult !== null) {
                rejectResult(error);
            }
        });
    }

    return {
        ready,
        run: (totalItems: number): Promise<number> => {
            const jobs: Promise<number>[] = [];
            const baseSize: number = Math.floor(totalItems / workerCount);
            const remainder: number = totalItems % workerCount;
            let start: number = 0;

            for (let i: number = 0; i < workerCount; i++) {
                const size: number = baseSize + (i < remainder ? 1 : 0);
                const end: number = start + size;
                jobs.push(new Promise((resolve: any, reject: any) => {
                    resultResolvers[i] = resolve;
                    resultRejecters[i] = reject;
                    workers[i].postMessage({ kind: "run", start, end });
                }));
                start = end;
            }

            return Promise.all(jobs).then((checksums: any) => {
                let checksum: number = 0;
                for (let i: number = 0; i < checksums.length; i++) {
                    checksum = checksum + checksums[i];
                }
                return checksum;
            });
        },
        close: (): Promise<number> => {
            const exits: Promise<number>[] = [];
            for (let i: number = 0; i < workers.length; i++) {
                exits.push(workers[i].terminate());
            }
            return Promise.all(exits).then((codes: any) => codes.length);
        },
    };
}
