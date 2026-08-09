import { trace } from "@sharpts/gui/internal-testing";
import { prefix } from "./lazy-dependency";

trace("tla-lazy-start");
export const value = prefix + await new Promise<number>(
    resolve => setTimeout(() => resolve(2), 2)
);
trace("tla-lazy-end");
queueMicrotask(() => trace("tla-lazy-microtask"));
