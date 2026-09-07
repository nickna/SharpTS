import {
    classConstruction,
    classFieldReuse,
    classMethodInheritanceBase,
    classMethodInherited,
    classMethodOverride,
    classMethodReuse,
    classMethodSuper,
} from "./lib/algorithms.ts";
import { bench } from "./lib/bench.ts";

const params: number[] = [1000, 10000, 100000];
for (let p: number = 0; p < params.length; p++) {
    const n: number = params[p];
    bench("class-field-reuse", n, () => classFieldReuse(n), n * (n + 1) / 2);
    bench("class-method-reuse", n, () => classMethodReuse(n), n * (n + 1) / 2);
    bench("class-method-inheritance-base", n, () => classMethodInheritanceBase(n), n * (n + 1) / 2);
    bench("class-method-inherited", n, () => classMethodInherited(n), n * (n + 1) / 2);
    bench("class-method-override", n, () => classMethodOverride(n), n * (n + 1) / 2);
    bench("class-method-super", n, () => classMethodSuper(n), n * (n + 1) / 2);
    bench("class-construction", n, () => classConstruction(n), n * (n - 1) / 2);
}
