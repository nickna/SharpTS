// Iterator-helper benchmarks (issue #90 hot path).
// `any[]` so the array is typed as List<object> in compiled mode — that's
// the path that goes through ArrayMap / ArrayFilter / etc., where my
// LoadArrayLikeElement indirection adds 1 Ldsfld + 1 Brfalse per element.
// Typed `number[]` (List<double>) takes a different ArrayEmitter path that
// is unaffected; it would't measure the regression I want to bound.

function arrMap(arr: any[]): any[] {
    return arr.map(x => x * 2);
}

function arrFilter(arr: any[]): any[] {
    return arr.filter(x => x > 10);
}

function arrReduce(arr: any[]): any {
    return arr.reduce((a, b) => a + b, 0);
}

function arrForEach(arr: any[]): any {
    let s: number = 0;
    arr.forEach(x => { s = s + x; });
    return s;
}

function arrEvery(arr: any[]): boolean {
    return arr.every(x => x >= 0);
}

function arrFind(arr: any[]): any {
    return arr.find(x => x > 9999) ?? -1;
}

// Phase A.2: variable-bound iterator callbacks. Same body shapes as the
// inline benchmarks above, but the callback comes through a top-level
// `const` binding. Validates that `arr.map(myFn)` reaches the same fast
// path as `arr.map(x => …)` when `myFn` is statically resolvable.
const cbDouble = (x) => x * 2;
const cbGtTen = (x) => x > 10;
const cbAdd = (a, b) => a + b;
const cbGteZero = (x) => x >= 0;
const cbGt9999 = (x) => x > 9999;

function arrMapBound(arr: any[]): any[] {
    return arr.map(cbDouble);
}

function arrFilterBound(arr: any[]): any[] {
    return arr.filter(cbGtTen);
}

function arrReduceBound(arr: any[]): any {
    return arr.reduce(cbAdd, 0);
}

function arrEveryBound(arr: any[]): boolean {
    return arr.every(cbGteZero);
}

function arrFindBound(arr: any[]): any {
    return arr.find(cbGt9999) ?? -1;
}

// Phase C: for-of over arrays. Reads each element via the iterator protocol
// today (Symbol.iterator probe + GetElement dispatch per iter); fast path
// should compile to direct Callvirt list[i].
function arrForOfSum(arr: any[]): any {
    let s: number = 0;
    for (const x of arr) {
        s = s + x;
    }
    return s;
}
