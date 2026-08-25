function numberToFixedLoop(n: number): number {
    let totalLength: number = 0;
    for (let i: number = 0; i < n; i++) {
        totalLength += (i * 0.125).toFixed(2).length;
    }
    return totalLength;
}
