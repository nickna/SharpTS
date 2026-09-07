function numericReadInput(): number[] {
    const values: number[] = [];
    for (let i: number = 0; i < 5; i++) values.push(1);
    return values;
}

function readFour(values: number[], index: number): number {
    return values[index] + values[index + 1] + values[index + 2] + values[index + 3];
}

function readFixed(values: number[], n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + readFour(values, 0);
    return sum;
}

function readVarying(values: number[], n: number): number {
    let sum: number = 0.5;
    for (let i: number = 0; i < n; i++) sum = sum + readFour(values, i % 2);
    return sum;
}
