import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const [workflowArgument, equivalentArgument, outputArgument] = process.argv.slice(2);
assert.ok(workflowArgument && equivalentArgument && outputArgument,
  "usage: node summarize.mjs <workflow-artifacts> <equivalent-analysis-artifacts> <compact-output.json>");
const read = async (root, name) => JSON.parse(await fs.readFile(path.join(root, name), "utf8"));
const workflow = await read(workflowArgument, "results.json");
const stdio = await read(workflowArgument, "stdio-results.json");
const baseline = await read(equivalentArgument, "baseline.json");
const current = await read(equivalentArgument, "current.json");
const compareReports = (oldReport, newReport) => oldReport.reports.map((before) => {
  const after = newReport.reports.find((report) => report.name === before.name);
  assert.ok(after);
  assert.equal(before.cold.resultFingerprint, after.cold.resultFingerprint);
  const compare = (phase) => ({ baseline: before[phase], current: after[phase],
    medianChangePercent: (after[phase].medianMilliseconds / before[phase].medianMilliseconds - 1) * 100,
    allocatedChangePercent: (after[phase].medianAllocatedBytes / before[phase].medianAllocatedBytes - 1) * 100 });
  return { name: before.name, cold: compare("cold"), warm: compare("warm"),
    retention: { baseline: before.retention, current: after.retention }, validation: after.validation };
});
const equivalent = compareReports(baseline, current);
let repeat;
try {
  const before = await read(equivalentArgument, "baseline-repeat.json"), after = await read(equivalentArgument, "current-repeat.json");
  repeat = { baselineTimestampUtc: before.timestampUtc, currentTimestampUtc: after.timestampUtc, reports: compareReports(before, after) };
} catch (error) { if (error.code !== "ENOENT") throw error; }
// This counter covers published SourceDocuments. Embedded default .d.ts modules
// currently have no SourceDocument and were never included in the measured count.
for (const item of workflow.reports) if (item.checkedDocumentsIncludingLibraries !== undefined) {
  item.sourceDocumentsInEntrySnapshot = item.checkedDocumentsIncludingLibraries;
  delete item.checkedDocumentsIncludingLibraries;
}
function compact(value) {
  if (Array.isArray(value)) return value.map(compact);
  if (value && typeof value === "object") return Object.fromEntries(Object.entries(value)
    .filter(([key]) => key !== "samples").map(([key, item]) => [key,
      key === "server" ? path.basename(item) : compact(item)]));
  return value;
}
const hash = async (file) => createHash("sha256").update(await fs.readFile(file)).digest("hex");
const runnerDirectory = path.dirname(fileURLToPath(import.meta.url));
const replaySourceSha256 = Object.fromEntries(await Promise.all(["Program.cs", "EditorWorkflow.csproj", "run.ps1", "stdio.mjs", "summarize.mjs"]
  .map(async (file) => [file, await hash(path.join(runnerDirectory, file))])));
const equivalentBinaries = {};
for (const name of ["baseline", "current"]) equivalentBinaries[name] = {
  languageServerSha256: await hash(path.join(equivalentArgument, "bin", name, "Release/net10.0/SharpTS.LanguageServer.dll")),
  compilerSha256: await hash(path.join(equivalentArgument, "bin", name, "Release/net10.0/SharpTS.dll")),
};
workflow.metadata.referenceAssemblyBytes = {
  core: (await fs.stat(path.join(equivalentArgument, "bin/current/Release/net10.0/SharpTS.dll"))).size,
  languageServer: (await fs.stat(path.join(equivalentArgument, "bin/current/Release/net10.0/SharpTS.LanguageServer.dll"))).size,
};
const report = { schemaVersion: 1, replaySourceSha256, workflow, stdio, equivalentAnalysis: {
  baselineCommit: "df4589b7", baselineTimestampUtc: baseline.timestampUtc, currentTimestampUtc: current.timestampUtc,
  environment: { runtime: baseline.runtime, os: baseline.os, architecture: baseline.architecture, processorCount: baseline.processorCount },
  binaries: equivalentBinaries, methodology: baseline.methodology, reports: equivalent, freshProcessRepeat: repeat,
}, limits: "Single-machine measurements. New feature sequences report absolute costs. Only the unchanged pre-existing definition/reference/statements/diagnostics sequence has an exact historical baseline. Allocation churn, estimated completed-cache bytes and full-GC live heap are distinct. Stdio counters are unobserved. Raw samples remain in ignored artifacts." };
await fs.writeFile(outputArgument, JSON.stringify(compact(report), null, 2) + "\n");
console.log(outputArgument);
