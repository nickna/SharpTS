import { double } from "./helper";

const delay = () => new Promise<number>((resolve): void => { setTimeout(() => resolve(0), 10); });

async function asynchronous(parameter: number): Promise<number> {
    let local = parameter + 1;
    let captured = parameter + 2;
    const reader = () => captured;
    console.log("async before", parameter, local, reader()); // @break:async-before
    await delay();
    console.log("async after", parameter, local, reader()); // @break:async-after
    {
        const blockLocal = local + 3;
        await delay();
        console.log("async block", blockLocal); // @break:async-block
    }
    captured++;
    console.log("async outside", local, reader()); // @break:async-outside
    {
        const local = parameter + 20;
        await delay();
        console.log("async shadow", local); // @break:async-shadow
    }
    console.log("async restored", local); // @break:async-restored
    {
        const parameter = 99;
        await delay();
        console.log("async parameter shadow", parameter); // @break:async-parameter-shadow
    }
    console.log("async parameter restored", parameter); // @break:async-parameter-restored
    let blockReader: () => number = () => 0;
    {
        let capturedBlock = parameter + 50;
        blockReader = () => capturedBlock;
        await delay();
        capturedBlock++;
        console.log("async captured block", blockReader()); // @break:async-captured-block
    }
    console.log("async captured outside", blockReader()); // @break:async-captured-outside
    {
        let local = 98;
        const shadowReader = () => local;
        local++;
        await delay();
        console.log("async captured shadow", shadowReader()); // @break:async-captured-shadow
    }
    console.log("async captured shadow restored", local); // @break:async-captured-shadow-restored
    {
        const repeated = parameter + 30;
        await delay();
        console.log("async sibling one", repeated); // @break:async-sibling-one
    }
    console.log("async between siblings", local); // @break:async-between-siblings
    {
        const repeated = parameter + 40;
        await delay();
        console.log("async sibling two", repeated); // @break:async-sibling-two
    }
    try {
        throw 7;
    } catch (error) {
        await delay();
        console.log("async catch", error); // @break:async-catch
    }
    console.log("async catch ended", local); // @break:async-catch-ended
    for (let index = 0; index < 1; index++) {
        await delay();
        console.log("async loop", index); // @break:async-loop
    }
    for (let item of [4]) {
        await delay();
        console.log("async for of let", item); // @break:async-for-of-let
    }
    let assigned = 0;
    for (assigned of [5]) {
        await delay();
        console.log("async for of assignment", assigned); // @break:async-for-of-assignment
    }
    var retained = 0;
    for (var retained of [6]) {
        await delay();
        console.log("async for of var", retained); // @break:async-for-of-var
    }
    console.log("async loop bindings ended", assigned, retained); // @break:async-loop-bindings-ended
    const [left, right] = [3, 4];
    var carried = double(left);
    const __user = left;
    const _destUser = right;
    await delay();
    console.log("async destructuring", left, right, carried, __user, _destUser); // @break:async-destructuring
    return local + reader();
}

function* sequence(parameter: number) {
    let local = parameter + 1;
    console.log("iterator before", parameter, local); // @break:iterator-before
    yield local;
    local += 2;
    console.log("iterator after", parameter, local); // @break:iterator-after
    {
        const blockLocal = local + 3;
        yield blockLocal;
        console.log("iterator block", blockLocal); // @break:iterator-block
    }
    console.log("iterator outside", local); // @break:iterator-outside
    {
        const local = parameter + 20;
        yield local;
        console.log("iterator shadow", local); // @break:iterator-shadow
    }
    console.log("iterator restored", local); // @break:iterator-restored
    for (let index = 0; index < 1; index++) {
        yield index;
        console.log("iterator loop", index); // @break:iterator-loop
    }
    console.log("iterator loop ended", local); // @break:iterator-loop-ended
}

async function* asynchronousSequence(parameter: number) {
    let local = parameter + 1;
    console.log("async iterator before", parameter, local); // @break:async-iterator-before
    await delay();
    yield local;
    local += 2;
    await delay();
    console.log("async iterator after", parameter, local); // @break:async-iterator-after
    {
        const blockLocal = local + 3;
        yield blockLocal;
        console.log("async iterator block", blockLocal); // @break:async-iterator-block
    }
    console.log("async iterator outside", local); // @break:async-iterator-outside
}

const asynchronousArrow = async (parameter: number): Promise<number> => {
    let local = parameter + 1;
    console.log("arrow before", parameter, local); // @break:arrow-before
    await delay();
    console.log("arrow after", parameter, local); // @break:arrow-after
    return local;
};

let moduleShared = 5;
const globalsArrow = async (parameter: number): Promise<number> => {
    const local = parameter + 1;
    await delay();
    console.log("global arrow", moduleShared, local); // @break:global-arrow
    return local;
};

async function capturedArrowOwner(seed: number): Promise<number> {
    const snapshot = seed + 1;
    let shared = seed + 2;
    const arrow = async (parameter: number): Promise<number> => {
        const local = parameter + snapshot;
        console.log("captured arrow before", seed, snapshot, shared, local); // @break:captured-arrow-before
        await delay();
        shared++;
        console.log("captured arrow after", seed, snapshot, shared, local); // @break:captured-arrow-after
        return local;
    };
    return await arrow(seed);
}

function synchronousArrowOwner(seed: number): Promise<number> {
    const snapshot = seed + 1;
    let shared = seed + 2;
    const arrow = async (parameter: number): Promise<number> => {
        const local = parameter + snapshot;
        console.log("sync owner arrow before", seed, snapshot, shared, local); // @break:sync-owner-arrow-before
        await delay();
        shared++;
        console.log("sync owner arrow after", seed, snapshot, shared, local); // @break:sync-owner-arrow-after
        return local;
    };
    return arrow(seed);
}

async function transitiveArrowOwner(seed: number): Promise<number> {
    let shared = seed + 1;
    const parent = async (parentParameter: number): Promise<number> => {
        const parentLocal = parentParameter + 1;
        const child = async (childParameter: number): Promise<number> => {
            const childLocal = childParameter + parentLocal;
            console.log("transitive before", seed, shared, parentLocal, childParameter, childLocal); // @break:transitive-before
            await delay();
            console.log("transitive after", seed, shared, parentLocal, childParameter, childLocal); // @break:transitive-after
            return childLocal;
        };
        await delay();
        return await child(parentLocal);
    };
    const pending = parent(seed + 2);
    shared++;
    return await pending;
}

async function numericCaptureLoop(n: number): Promise<number> {
    let i: number = 7;
    await delay();
    let chain: Promise<number> = Promise.resolve(0);
    {
        for (let i: number = 0; i < n; i++) {
            chain = chain.then((sum: number): number => sum + i);
            console.log("numeric capture loop", i); // @break:numeric-capture-loop
        }
    }
    const result = await chain;
    console.log("numeric capture restored", i, result); // @break:numeric-capture-restored
    return result;
}

class Cases {
    async work(parameter: number): Promise<number> {
        const local = parameter + 1;
        await delay();
        console.log("instance async", parameter, local); // @break:instance-async
        return local;
    }

    static async work(parameter: number): Promise<number> {
        const local = parameter + 1;
        await delay();
        console.log("static async", parameter, local); // @break:static-async
        return local;
    }

    *sequence(parameter: number) {
        const local = parameter + 1;
        yield local;
        console.log("instance iterator", parameter, local); // @break:instance-iterator
    }

    async *stream(parameter: number) {
        const local = parameter + 1;
        await delay();
        yield local;
        console.log("instance async iterator", parameter, local); // @break:instance-async-iterator
    }
}

namespace Named {
    export async function work(parameter: number): Promise<number> {
        const local = parameter + 1;
        await delay();
        console.log("namespace async", parameter, local); // @break:namespace-async
        return local;
    }

    export function* sequence(parameter: number) {
        const local = parameter + 1;
        yield local;
        console.log("namespace iterator", parameter, local); // @break:namespace-iterator
    }

    export async function* stream(parameter: number) {
        const local = parameter + 1;
        await delay();
        yield local;
        console.log("namespace async iterator", parameter, local); // @break:namespace-async-iterator
    }
}

async function run(): Promise<void> {
    await asynchronous(10);
    for (const value of sequence(20)) console.log("iterator result", value);
    for await (const value of asynchronousSequence(30)) console.log("async iterator result", value);
    console.log("arrow result", await asynchronousArrow(40));
    moduleShared = 9;
    console.log("global arrow result", await globalsArrow(42));
    console.log("captured arrow result", await capturedArrowOwner(45));
    console.log("sync owner arrow result", await synchronousArrowOwner(46));
    console.log("transitive arrow result", await transitiveArrowOwner(10));
    console.log("numeric capture result", await numericCaptureLoop(3));
    const cases = new Cases();
    await cases.work(50);
    await Cases.work(60);
    for (const value of cases.sequence(70)) console.log("instance iterator result", value);
    for await (const value of cases.stream(80)) console.log("instance stream result", value);
    await Named.work(90);
    for (const value of Named.sequence(100)) console.log("namespace iterator result", value);
    for await (const value of Named.stream(110)) console.log("namespace stream result", value);
}

run().then(() => console.log("completed"));
