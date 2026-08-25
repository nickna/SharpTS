// Stable Date numeric getter/setter workload (#1487). One Date allocation is
// intentionally outside the loop; Date helper results must not allocate per
// iteration.
function dateNumericLoop(n: number): number {
    const date = new Date(0);
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        date.setTime(i);
        sum = sum + date.getTime();
    }
    return sum;
}
