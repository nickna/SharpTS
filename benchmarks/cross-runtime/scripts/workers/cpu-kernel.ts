const CHECKSUM_MODULUS: number = 1000000007;

// Deliberately allocation-free integer work that can be split into independent
// ranges. The inner recurrence keeps each item expensive enough that worker
// dispatch is a small part of the steady-state measurement.
export function cpuRangeChecksum(start: number, end: number): number {
    let checksum: number = 0;
    for (let item: number = start; item < end; item++) {
        let value: number = item + 1;
        for (let round: number = 0; round < 32; round++) {
            value = (value * 1664525 + 1013904223) % 2147483647;
            checksum = (checksum + value) % CHECKSUM_MODULUS;
        }
    }
    return checksum;
}

export function combineChecksums(checksums: number[]): number {
    let combined: number = 0;
    for (let i: number = 0; i < checksums.length; i++) {
        combined = (combined + checksums[i]) % CHECKSUM_MODULUS;
    }
    return combined;
}
