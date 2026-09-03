function throwBranchControl(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) sum = sum + ((i & (throwEvery - 1)) === 0 ? i : 1);
    return sum;
}

function throwTryCatchNoThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try { sum = sum + ((i & (throwEvery - 1)) === 0 ? i : 1); }
        catch (error) { sum = sum - 1; }
    }
    return sum;
}

function throwPrimitiveLocal(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & (throwEvery - 1)) === 0) throw i;
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function returnPrimitiveFromCallee(value: number): number { return value; }
function throwPrimitiveFromCallee(value: number): void { throw value; }

function throwCalleeNoThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try { sum = sum + ((i & (throwEvery - 1)) === 0 ? returnPrimitiveFromCallee(i) : 1); }
        catch (error) { sum = sum - 1; }
    }
    return sum;
}

function throwPrimitiveCallee(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & (throwEvery - 1)) === 0) throwPrimitiveFromCallee(i);
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function throwFinallyNoThrow(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            try { sum = sum + ((i & (throwEvery - 1)) === 0 ? i : 1); }
            finally { sum = sum + 0; }
        } catch (error) { sum = sum - 1; }
    }
    return sum;
}

function throwPrimitiveThroughFinally(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            try {
                if ((i & (throwEvery - 1)) === 0) throw i;
                sum = sum + 1;
            } finally { sum = sum + 0; }
        } catch (error: any) {
            sum = sum + (error === i ? i : -1);
        }
    }
    return sum;
}

function throwErrorSparse(n: number, throwEvery: number): number {
    let sum: number = 0;
    for (let i: number = 0; i < n; i++) {
        try {
            if ((i & (throwEvery - 1)) === 0) throw new Error("sparse");
            sum = sum + 1;
        } catch (error: any) {
            sum = sum + (error instanceof Error ? i : -1);
        }
    }
    return sum;
}

function constructErrorSparse(n: number, throwEvery: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & (throwEvery - 1)) === 0) {
            const error = new Error("sparse");
            total = total + error.message.length;
        }
    }
    return total;
}

function firstErrorStackRead(n: number, throwEvery: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & (throwEvery - 1)) === 0) total = total + new Error("sparse").stack!.length;
    }
    return total;
}

function repeatedErrorStackRead(n: number, throwEvery: number): number {
    let total: number = 0;
    for (let i: number = 0; i < n; i++) {
        if ((i & (throwEvery - 1)) === 0) {
            const error = new Error("sparse");
            const first = error.stack!;
            const second = error.stack!;
            total = total + (first === second ? second.length : -1);
        }
    }
    return total;
}
