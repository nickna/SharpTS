import { bench, benchAsync, listCases, shouldRunCase } from "./lib/bench.ts";
import { createWorkerPool } from "./lib/worker-pool.ts";
import { allocationChecksum } from "./workers/allocation-kernel.ts";
import { expectedAllocationChecksum } from "./workers/allocation-expected.ts";

// Names identify partition count; param is records per partition. Thus total
// work = param * partitions, identical in each paired serial/worker case.
async function runCase(partitions: number, items: number): Promise<void> {
    const serialName: string = "allocation-partitions-serial-" + partitions;
    const workerName: string = "allocation-partitions-workers-" + partitions;
    if (listCases) {
        console.log("BENCH_CASE:" + serialName);
        console.log("BENCH_CASE:" + workerName);
        return;
    }
    const expected: number = expectedAllocationChecksum(0, items * partitions);
    bench(serialName, items, () => {
        let sum: number = 0;
        for (let i: number = 0; i < partitions; i++) {
            sum = sum + allocationChecksum(i * items, (i + 1) * items);
        }
        return sum;
    }, expected);
    if (!shouldRunCase(workerName, items)) return;
    const meta: any = import.meta;
    const pool: any = createWorkerPool(partitions, meta.dirname + "/workers/allocation-worker.ts");
    try {
        await pool.ready;
        await benchAsync(workerName, items, () => pool.run(items * partitions), expected);
    } finally {
        await pool.close();
    }
}

async function main(): Promise<void> {
    const partitions: number[] = [1, 2, 4];
    const sizes: number[] = [8192, 8193, 20000];
    for (let i: number = 0; i < partitions.length; i++) {
        for (let j: number = 0; j < sizes.length; j++) {
            if (!listCases || j === 0) await runCase(partitions[i], sizes[j]);
        }
    }
}
main().catch((error: any) => { console.error(error); process.exitCode = 1; });
