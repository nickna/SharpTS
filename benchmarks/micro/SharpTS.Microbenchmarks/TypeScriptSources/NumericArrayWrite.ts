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
