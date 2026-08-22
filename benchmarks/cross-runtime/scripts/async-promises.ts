import {
    asyncFunctionCalls,
    asyncSequentialAwait,
    promiseAllFanOut,
    promiseThenChain,
} from "./lib/algorithms.ts";
import { benchAsync } from "./lib/bench.ts";

async function main(): Promise<void> {
    const params: number[] = [10, 100, 1000];
    for (let p: number = 0; p < params.length; p++) {
        const n: number = params[p];
        await benchAsync("async-resolved-await", n, () => asyncSequentialAwait(n));
        await benchAsync("async-function-calls", n, () => asyncFunctionCalls(n));
        await benchAsync("promise-then-chain", n, () => promiseThenChain(n));
        await benchAsync("promise-all", n, () => promiseAllFanOut(n));
    }
}

main();
