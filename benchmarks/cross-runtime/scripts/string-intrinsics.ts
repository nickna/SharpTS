import { bench } from "./lib/bench.ts";

const input: string = "alpha-beta-gamma-delta";
const needle: string = "gamma";
const position: number = 2;
const start: number = 3;
const end: number = 18;
const params: number[] = [100, 10000, 100000];

for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];

    bench("string-indexof", n, () => {
        let total: number = 0;
        let currentPosition: number = position;
        for (let i: number = 0; i < n; i++) {
            total = total + input.indexOf(needle, currentPosition);
            currentPosition = currentPosition === position ? 12 : position;
        }
        return total;
    });

    bench("string-includes", n, () => {
        let total: number = 0;
        let currentPosition: number = position;
        for (let i: number = 0; i < n; i++) {
            if (input.includes(needle, currentPosition)) {
                total = total + 1;
            }
            currentPosition = currentPosition === position ? 12 : position;
        }
        return total;
    });

    bench("string-slice", n, () => {
        let total: number = 0;
        let currentStart: number = start;
        for (let i: number = 0; i < n; i++) {
            total = total + input.slice(currentStart, end).length;
            currentStart = currentStart === start ? start + 1 : start;
        }
        return total;
    });

    bench("string-substring", n, () => {
        let total: number = 0;
        let currentStart: number = start;
        for (let i: number = 0; i < n; i++) {
            total = total + input.substring(currentStart, end).length;
            currentStart = currentStart === start ? start + 1 : start;
        }
        return total;
    });
}
