function arrayShiftDrain(n: number): number {
    const values: number[] = [];
    for (let i: number = 0; i < n; i++) {
        values.push(i);
    }
    let checksum: number = 0;
    while (values.length > 0) {
        checksum = checksum + values.shift();
    }
    return checksum;
}

function arrayUnshiftBuild(n: number): number {
    const values: number[] = [];
    for (let i: number = 0; i < n; i++) {
        values.unshift(i);
    }
    return values.length + values[0] + values[n - 1];
}
