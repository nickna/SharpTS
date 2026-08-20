// Models the cross-runtime bench(...) callback boundary without putting its
// timing loop inside a BenchmarkDotNet operation.
export function invokeBenchmark(callback: () => number): number {
    return callback();
}
