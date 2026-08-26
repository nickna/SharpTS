import { bench } from "./lib/bench.ts";

function exactRecordKeys(n: number): number {
    const exactKeysRecord = { alpha: 1, beta: 2, gamma: 3, delta: 4 };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const exactKeys: string[] = Object.keys(exactKeysRecord);
        checksum = checksum + exactKeys.length;
    }
    return checksum;
}

function numericRecordKeys(n: number): number {
    const numericKeysRecord: any = { 10: "ten", first: "a", 2: "two", 1: "one" };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const numericKeys: string[] = Object.keys(numericKeysRecord);
        if (numericKeys.join(",") === "1,2,10,first") checksum = checksum + 1;
    }
    return checksum;
}

function mutatedRecordKeys(n: number): number {
    const mutatedKeysRecord: any = { first: 1, second: 2 };
    mutatedKeysRecord.third = 3;
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const mutatedKeys: string[] = Object.keys(mutatedKeysRecord);
        checksum = checksum + mutatedKeys.length;
    }
    return checksum;
}

function accessorRecordKeys(n: number): number {
    let getterCalls: number = 0;
    const accessorKeysRecord: any = {
        first: 1,
        get second(): number { getterCalls = getterCalls + 1; return 2; }
    };
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const accessorKeys: string[] = Object.keys(accessorKeysRecord);
        checksum = checksum + accessorKeys.length;
    }
    return checksum + getterCalls;
}

function proxyRecordKeys(n: number): number {
    let ownKeysCalls: number = 0;
    const proxyKeysRecord: any = new Proxy({ first: 1, second: 2 }, {
        ownKeys(target: any): any[] {
            ownKeysCalls = ownKeysCalls + 1;
            return Reflect.ownKeys(target);
        }
    });
    let checksum: number = 0;
    for (let i: number = 0; i < n; i++) {
        const proxyKeys: string[] = Object.keys(proxyKeysRecord);
        checksum = checksum + proxyKeys.length;
    }
    return checksum + ownKeysCalls;
}

const sizes: number[] = [10000, 100000];
for (let i: number = 0; i < sizes.length; i++) {
    const n: number = sizes[i];
    bench("object-keys-exact", n, () => exactRecordKeys(n));
    bench("object-keys-numeric", n, () => numericRecordKeys(n));
    bench("object-keys-mutated", n, () => mutatedRecordKeys(n));
    bench("object-keys-accessor", n, () => accessorRecordKeys(n));
    bench("object-keys-proxy", n, () => proxyRecordKeys(n));
}
