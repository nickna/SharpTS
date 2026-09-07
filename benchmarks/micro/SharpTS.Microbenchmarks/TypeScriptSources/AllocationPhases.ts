// Phase diagnostics for allocation-kernel.ts. FullKernel benchmarks the original
// shared kernel; these separate functions deliberately expose its retained graph.
interface PhaseAllocationRecord {
    index: number;
    next: number;
    label: string;
    values: number[];
}

function buildAllocationRecords(end: number): any {
    const records: PhaseAllocationRecord[] = [];
    for (let i: number = 0; i < end; i++) {
        records.push({
            index: i,
            next: i + 1,
            label: "item-" + (i % 100),
            values: [i, i + 1, i + 2, i + 3],
        });
    }
    return records;
}

function readAllocationRecords(input: any): number {
    const records: PhaseAllocationRecord[] = input;
    let checksum: number = 0;
    for (let i: number = 0; i < records.length; i++) {
        const record: PhaseAllocationRecord = records[i];
        checksum = checksum + record.index + record.next + record.label.length +
            record.values[0] + record.values[3];
    }
    return checksum;
}
