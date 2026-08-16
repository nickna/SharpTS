import { trace } from "@sharpts/gui/conformance";

function recordDependencyMicrotask(): void {
    trace("tla-dependency-microtask");
}

trace("tla-dependency-start");
export const prefix = await Promise.resolve(40);
queueMicrotask(recordDependencyMicrotask as any);
