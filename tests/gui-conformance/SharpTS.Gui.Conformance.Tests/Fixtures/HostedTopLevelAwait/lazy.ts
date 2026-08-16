import { trace } from "@sharpts/gui/conformance";
import { prefix } from "./lazy-dependency";

function recordLazyMicrotask(): void {
    trace("tla-lazy-microtask");
}

trace("tla-lazy-start");
export const value = prefix + await Promise.resolve(2);
trace("tla-lazy-end");
queueMicrotask(recordLazyMicrotask as any);
