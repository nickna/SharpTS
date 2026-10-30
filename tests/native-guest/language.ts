function sum(values: number[]): number {
    let result = 0;
    for (let i = 0; i < values.length; i++) result += values[i];
    return result;
}
const values = [1, 2, 3];
console.log(sum(values));
console.log(values.map((value: number) => value * 2).join(','));
console.log(JSON.stringify({ answer: 42, ok: true }));
const doubled = (value: number) => value * 2;
console.log(doubled(21));
