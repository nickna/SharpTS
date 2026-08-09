import { trace } from "@sharpts/gui/internal-testing";

trace("tla-dependency-start");
export const prefix = await Promise.resolve(40);
queueMicrotask(() => trace("tla-dependency-microtask"));
