import { createWorkerPool } from "../../scripts/lib/worker-pool.ts";
import { allocationChecksum } from "../../scripts/workers/allocation-kernel.ts";
import { performance } from "perf_hooks";

// Fixed job count matches StartupHook's GC_PHASE normalization. Do not include
// startup, worker compilation, readiness or warmup in the allocation interval.
async function main(): Promise<void> {
    const workers: number = Number(process.env.PROFILE_WORKERS || "0");
    if (workers !== 0 && workers !== 1 && workers !== 2 && workers !== 4)
        throw new Error("PROFILE_WORKERS must be 0, 1, 2 or 4");
    const meta: any = import.meta;
    const pool: any = workers === 0 ? null : createWorkerPool(workers,
        meta.dirname + "/../../scripts/workers/allocation-worker.ts");
    try {
        if (pool !== null) await pool.ready;
        const start: number = performance.now();
        let result: number = 0;
        do {
            result = pool === null ? allocationChecksum(0, 20000) : await pool.run(20000);
            if (result !== 800178000) throw new Error("warmup checksum");
        } while (performance.now() - start < 1000);
        console.log("GC_BEGIN");
        for (let i: number = 0; i < 20; i++) {
            result = pool === null ? allocationChecksum(0, 20000) : await pool.run(20000);
            if (result !== 800178000) throw new Error("phase checksum");
        }
        console.log("GC_END");
    } finally {
        if (pool !== null) await pool.close();
    }
}
main().catch((error: any) => { console.error(error); process.exitCode = 1; });
