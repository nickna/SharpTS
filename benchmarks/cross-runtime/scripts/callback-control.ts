import { bench, benchAsync } from "./lib/bench.ts";

// Diagnostic floor for callback routing and harness overhead. Do not subtract
// this from other cases: inlining and tiering differ with the callback body.
bench("empty-callback", 0, () => 0, 0);

async function main(): Promise<void> {
    await benchAsync("empty-async-callback", 0, async (): Promise<number> => 0, 0);
}
main();
