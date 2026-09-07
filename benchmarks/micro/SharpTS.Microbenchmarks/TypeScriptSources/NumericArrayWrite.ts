function fillIndex(a: number[], n: number): void {
    for (let i: number = 0; i < n; i++) {
        a[i] = i * 3;
    }
}

export function numericArrayWrite(n: number): number {
    const a: number[] = [];
    fillIndex(a, n);
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + a[i];
    }
    return sum;
}

let overwriteArray: number[] = [];
let readArray: number[] = [];

export function setupNumericArrays(n: number): number {
    const overwrite: number[] = [];
    const read: number[] = [];
    fillIndex(overwrite, n);
    fillIndex(read, n);
    overwriteArray = overwrite;
    readArray = read;
    return n;
}

function checksumIndex(a: number[], n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        sum = sum + a[i];
    }
    return sum;
}

export function numericArrayOverwrite(n: number): number {
    fillIndex(overwriteArray, n);
    return checksumIndex(overwriteArray, n);
}

export function numericArrayRead(n: number): number {
    return checksumIndex(readArray, n);
}
