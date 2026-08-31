interface AllocationRecord {
    index: number;
    next: number;
    label: string;
    values: number[];
}

export function allocationChecksum(start: number, end: number): number {
    const records: AllocationRecord[] = [];
    for (let i: number = start; i < end; i++) {
        records.push({
            index: i,
            next: i + 1,
            label: "item-" + (i % 100),
            values: [i, i + 1, i + 2, i + 3],
        });
    }

    let checksum: number = 0;
    for (let i: number = 0; i < records.length; i++) {
        const record: AllocationRecord = records[i];
        checksum = checksum + record.index + record.next + record.label.length +
            record.values[0] + record.values[3];
    }
    return checksum;
}
