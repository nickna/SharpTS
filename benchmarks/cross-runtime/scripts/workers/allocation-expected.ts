// Independent arithmetic oracle for nonnegative integer ranges. Each record
// contributes 4*i + 4 plus a label of length 6 (residue < 10) or 7.
export function expectedAllocationChecksum(start: number, end: number): number {
    const n: number = end - start;
    const shortBeforeStart: number = Math.floor(start / 100) * 10 + Math.min(start % 100, 10);
    const shortBeforeEnd: number = Math.floor(end / 100) * 10 + Math.min(end % 100, 10);
    return 2 * n * (start + end - 1) + 11 * n - (shortBeforeEnd - shortBeforeStart);
}
