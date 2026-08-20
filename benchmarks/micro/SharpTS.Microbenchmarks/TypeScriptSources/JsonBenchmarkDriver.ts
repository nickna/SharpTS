import {
    jsonBuildPhase,
    jsonStringifyPhase,
    jsonParsePhase,
    jsonRoundTrip
} from "./algorithms.ts";
import { invokeBenchmark } from "./benchmark-callback.ts";

// Keep n captured by an arrow callback as it is in cross-runtime json.ts. Each
// export invokes only once so BenchmarkDotNet owns the measurement loop.
export function importedJsonBuildPhase(n: number): number {
    return invokeBenchmark((): number => jsonBuildPhase(n));
}

export function importedJsonStringifyPhase(n: number): number {
    return invokeBenchmark((): number => jsonStringifyPhase(n));
}

export function importedJsonParsePhase(n: number): number {
    return invokeBenchmark((): number => jsonParsePhase(n));
}

export function importedJsonRoundTrip(n: number): number {
    return invokeBenchmark((): number => jsonRoundTrip(n));
}
