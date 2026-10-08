import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

export async function readTrace(workspace) {
  const config = JSON.parse(await fs.readFile(path.join(workspace, "test-config.json"), "utf8"));
  const messages = (await fs.readFile(config.log, "utf8")).split(/\r?\n/).flatMap((line, index) => {
    const match = line.match(/\bsharpts (->|<-) (\{.*\})$/);
    return match ? [{ direction: match[1], message: JSON.parse(match[2]), index }] : [];
  });
  const documents = new Map(), histories = [];
  const offset = (text, position) => {
    let start = 0;
    for (let line = 0; line < position.line; line++) start = text.indexOf("\n", start) + 1;
    return start + position.character;
  };
  for (const item of messages) {
    if (item.direction !== "->") continue;
    const { method, params } = item.message;
    if (method === "textDocument/didOpen") documents.set(params.textDocument.uri.toLowerCase(), { ...params.textDocument });
    if (method === "textDocument/didChange") {
      const state = documents.get(params.textDocument.uri.toLowerCase());
      for (const change of params.contentChanges) {
        state.text = change.range
          ? state.text.slice(0, offset(state.text, change.range.start)) + change.text + state.text.slice(offset(state.text, change.range.end))
          : change.text;
      }
      state.version = params.textDocument.version;
    }
    if (["textDocument/didOpen", "textDocument/didChange", "textDocument/didSave"].includes(method)) {
      const state = documents.get(params.textDocument.uri.toLowerCase());
      if (state) histories.push({ ...state, index: item.index, method });
    }
  }
  const requests = (method) => messages.filter((item) => item.direction === "->" && item.message.method === method).map((request) => ({
    request, response: messages.find((item) => item.direction === "<-" && item.index > request.index && item.message.id === request.message.id),
    document: histories.findLast((item) => item.index < request.index && item.uri.toLowerCase() === request.message.params.textDocument?.uri?.toLowerCase()),
  }));
  return { config, messages, documents, histories, requests };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url) && process.argv[2]) {
  const trace = await readTrace(path.resolve(process.argv[2]));
  console.log(JSON.stringify({ documents: [...trace.documents.values()], lastRequests: trace.messages.filter((item) => item.direction === "->" && item.message.id !== undefined).slice(-6) }, null, 2));
}
