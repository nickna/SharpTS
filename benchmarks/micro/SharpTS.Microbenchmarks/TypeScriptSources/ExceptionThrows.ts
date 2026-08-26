function throwBranchControl(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            sum = sum + i;
        } else {
            sum = sum + 1;
        }
    }
    return sum;
}

function throwTryCatchNoThrow(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & 1023) === 0) {
                sum = sum + i;
            } else {
                sum = sum + 1;
            }
        } catch (error) {
            sum = sum - 1;
        }
    }
    return sum;
}

function throwPrimitiveSparse(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & 1023) === 0) {
                throw i;
            }
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function throwErrorSparse(n: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & 1023) === 0) {
                throw new Error("sparse");
            }
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error instanceof Error ? i : -1);
        }
    }
    return sum;
}

function constructErrorSparse(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            const error = new Error("sparse");
            total = total + error.message.length;
        }
    }
    return total;
}

function firstErrorStackRead(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            const error = new Error("sparse");
            total = total + error.stack!.length;
        }
    }
    return total;
}

function repeatedErrorStackRead(n: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & 1023) === 0) {
            const error = new Error("sparse");
            const first = error.stack!;
            const second = error.stack!;
            total = total + (first === second ? second.length : -1);
        }
    }
    return total;
}
