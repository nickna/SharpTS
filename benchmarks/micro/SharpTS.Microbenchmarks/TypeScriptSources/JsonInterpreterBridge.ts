import {
    importedJsonBuildPhase,
    importedJsonStringifyPhase,
    importedJsonParsePhase,
    importedJsonRoundTrip
} from "./json-driver.ts";

// BenchmarkDotNet needs stable handles after module initialization. Publishing
// the already-imported functions on this realm's global object does not alter
// the measured call shape: each function still crosses the live import and
// capturing-callback boundary in JsonBenchmarkDriver.ts.
(globalThis as any).__sharpTSJsonImportedBuild = importedJsonBuildPhase;
(globalThis as any).__sharpTSJsonImportedStringify = importedJsonStringifyPhase;
(globalThis as any).__sharpTSJsonImportedParse = importedJsonParsePhase;
(globalThis as any).__sharpTSJsonImportedRoundTrip = importedJsonRoundTrip;
