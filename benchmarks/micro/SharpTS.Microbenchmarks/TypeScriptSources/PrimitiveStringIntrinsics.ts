function stringIndexOfLoop(
    input: string, needle: string, position: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + input.indexOf(needle, position);
    }
    return total;
}

function stringIncludesLoop(
    input: string, needle: string, position: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if (input.includes(needle, position)) {
            total = total + 1;
        }
    }
    return total;
}

function stringSliceLoop(
    input: string, start: number, end: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + input.slice(start, end).length;
    }
    return total;
}

function stringSubstringLoop(
    input: string, start: number, end: number, n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        total = total + input.substring(start, end).length;
    }
    return total;
}
