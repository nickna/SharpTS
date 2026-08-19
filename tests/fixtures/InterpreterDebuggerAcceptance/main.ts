import { Counter, afterAwait, makeClosure, values } from "./library";
import { Worker } from "worker_threads";

const counter = new Counter(1);
console.log(`class=${counter.increment()}`);

const addCaptured = makeClosure(10);
for (let index = 0; index < 2; index++) {
    // A breakpoint on this comment must move to the conditional below.
    if (index === 1) {
        console.log(`closure=${addCaptured(5)}`);
    }
}

try {
    throw new Error("acceptance");
} catch (error) {
    console.log("caught=acceptance");
} finally {
    console.log("finally=ran");
}

async function rejectLater(): Promise<void> {
    await Promise.resolve();
    throw new Error("unhandled acceptance");
}

const exceptionMode = process.env.SHARPTS_DAP_EXCEPTION;
if (exceptionMode === "uncaught") {
    throw new Error("uncaught acceptance");
}
if (exceptionMode === "unhandled") {
    setTimeout(rejectLater as any, 0);
}

console.log(`args=${process.argv.slice(-2).join(",")}`);
console.log(`env=${process.env.SHARPTS_DAP_ACCEPTANCE}`);

async function exerciseAsync(): Promise<void> {
    console.log(`async=${await afterAwait()}`);

    const iterator = values();
    iterator.next();
    console.log(`yield=${iterator.next().value}`);

    await Promise.resolve().then((() => console.log("promise=microtask")) as any);
    await new Promise<void>((resolve) => setTimeout((() => {
        console.log("timer=callback");
        resolve();
    }) as any, 0));

    new Worker(__dirname + "/worker.ts");
}

exerciseAsync();
