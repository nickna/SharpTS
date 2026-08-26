import { bench } from "./lib/bench.ts";

const input: string = "foo bar foo baz foo qux";
const params: number[] = [100, 10000, 100000];

for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("regex-replace", n, () => {
        let total: number = 0;
        for (let i: number = 0; i < n; i++) {
            total = total + input.replace(/foo/g, "bar").length;
        }
        return total;
    });
}
