import { bench } from "./lib/bench.ts";

// Stable, non-global RegExp.prototype.test validator workload (#1484).
// Keep the literal directly in its consuming position so compiled SharpTS can
// prove that the wrapper neither escapes nor carries observable lastIndex state.
function regexValidatorLoop(input: string, n: number): number {
    let valid: number = 0;
    for (let i: number = 0; i < n; i++) {
        if (/^[a-z]+$/.test(input)) {
            valid = valid + 1;
        }
    }
    return valid;
}

const input: string = "abcdefghij";
const params: number[] = [100, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("regex-validator", n, () => regexValidatorLoop(input, n));
}
