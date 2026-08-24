import {
    classConstruction,
    classFieldReuse,
    classMethodReuse,
} from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [1000, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("class-field-reuse", n, () => classFieldReuse(n));
    bench("class-method-reuse", n, () => classMethodReuse(n));
    bench("class-construction", n, () => classConstruction(n));
}
