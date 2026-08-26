import { bench } from "./lib/bench.ts";

function scanIncludes(n: number): number {
    const values: number[] = [];
    for (let i: number = 0; i < n; i++) values.push(i);

    let checksum: number = 0;
    for (let pass: number = 0; pass < 100; pass++) {
        if (values.includes(-1)) checksum = checksum + 1;
    }

    if (values.includes(n - 1)) checksum = checksum + 1;       // hit
    if (!values.includes(n + 1)) checksum = checksum + 2;      // miss
    if (values.includes(n - 1, n - 1)) checksum = checksum + 4;
    if (!values.includes(0, 1)) checksum = checksum + 8;       // positive fromIndex
    if (values.includes(n - 1, -1)) checksum = checksum + 16;  // negative fromIndex

    const special: number[] = [];
    special.push(-0, NaN);
    if (special.includes(+0)) checksum = checksum + 32;        // signed zero
    if (special.includes(NaN)) checksum = checksum + 64;       // SameValueZero NaN

    const empty: number[] = [];
    if (!empty.includes(0)) checksum = checksum + 128;
    return checksum;
}

const sizes: number[] = [1000, 10000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("array-includes", n, () => scanIncludes(n));
}
