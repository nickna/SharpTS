export type PaintTool = "brush" | "eraser" | "line" | "rectangle" | "ellipse";
export interface PaintPoint { readonly x: number; readonly y: number; }
export type PaintCommand =
    | { readonly kind: "polyline"; readonly points: readonly PaintPoint[]; readonly stroke: string; readonly strokeThickness: number; readonly lineCap: "round"; readonly lineJoin: "round"; readonly composite?: "destinationOut" }
    | { readonly kind: "line"; readonly x1: number; readonly y1: number; readonly x2: number; readonly y2: number; readonly stroke: string; readonly strokeThickness: number }
    | { readonly kind: "rectangle"; readonly x: number; readonly y: number; readonly width: number; readonly height: number; readonly fill?: string; readonly stroke?: string; readonly strokeThickness: number }
    | { readonly kind: "ellipse"; readonly centerX: number; readonly centerY: number; readonly radiusX: number; readonly radiusY: number; readonly fill?: string; readonly stroke?: string; readonly strokeThickness: number }
    | { readonly kind: "image"; readonly source: string; readonly x: number; readonly y: number; readonly width: number; readonly height: number };

export interface PaintLayer {
    readonly id: string;
    readonly name: string;
    readonly isVisible: boolean;
    readonly opacity: number;
    readonly commands: readonly PaintCommand[];
}

export interface PaintDocument {
    readonly format: "sharpaint";
    readonly version: 1;
    readonly width: number;
    readonly height: number;
    readonly layers: readonly PaintLayer[];
}

export interface DocumentHistory {
    readonly document: PaintDocument;
    readonly savedDocument: PaintDocument;
    readonly past: readonly PaintDocument[];
    readonly future: readonly PaintDocument[];
    readonly dirty: boolean;
}

export interface PaintDraft {
    readonly tool: PaintTool;
    readonly start: PaintPoint;
    readonly points: readonly PaintPoint[];
}

let nextLayerNumber = 1;
export function createLayerId(): string { return "layer-" + nextLayerNumber++; }

export function createDocument(width: number = 1024, height: number = 768): PaintDocument {
    validateDimension(width, "width");
    validateDimension(height, "height");
    return {
        format: "sharpaint",
        version: 1,
        width,
        height,
        layers: [{
            id: createLayerId(),
            name: "Background",
            isVisible: true,
            opacity: 1,
            commands: [{ kind: "rectangle", x: 0, y: 0, width, height, fill: "#ffffff", strokeThickness: 1 }],
        }],
    };
}

export function createImportedDocument(width: number, height: number, dataUri: string): PaintDocument {
    const document = createDocument(width, height);
    return {
        ...document,
        layers: [{
            id: document.layers[0].id,
            name: "Imported image",
            isVisible: true,
            opacity: 1,
            commands: [{ kind: "image", source: dataUri, x: 0, y: 0, width, height }],
        }],
    };
}

export function createHistory(document: PaintDocument): DocumentHistory {
    return { document, savedDocument: document, past: [], future: [], dirty: false };
}

export function commitDocument(history: DocumentHistory, document: PaintDocument): DocumentHistory {
    if (document === history.document) return history;
    const past = history.past.length >= 50 ? history.past.slice(history.past.length - 49) : history.past.slice();
    return { ...history, document, past: [...past, history.document], future: [], dirty: document !== history.savedDocument };
}

export function undo(history: DocumentHistory): DocumentHistory {
    if (history.past.length === 0) return history;
    const previous = history.past[history.past.length - 1];
    return {
        document: previous,
        savedDocument: history.savedDocument,
        past: history.past.slice(0, history.past.length - 1),
        future: [history.document, ...history.future],
        dirty: previous !== history.savedDocument,
    };
}

export function redo(history: DocumentHistory): DocumentHistory {
    if (history.future.length === 0) return history;
    const next = history.future[0];
    const past = history.past.length >= 50 ? history.past.slice(history.past.length - 49) : history.past.slice();
    return {
        document: next,
        savedDocument: history.savedDocument,
        past: [...past, history.document],
        future: history.future.slice(1),
        dirty: next !== history.savedDocument,
    };
}

export function markSaved(history: DocumentHistory): DocumentHistory {
    return { ...history, savedDocument: history.document, dirty: false };
}

export function clampPoint(document: PaintDocument, point: PaintPoint): PaintPoint {
    return {
        x: Math.max(0, Math.min(document.width, point.x)),
        y: Math.max(0, Math.min(document.height, point.y)),
    };
}

export function beginDraft(tool: PaintTool, point: PaintPoint): PaintDraft {
    return { tool, start: point, points: [point] };
}

export function extendDraft(draft: PaintDraft, point: PaintPoint): PaintDraft {
    const last = draft.points[draft.points.length - 1];
    const dx = point.x - last.x;
    const dy = point.y - last.y;
    if (dx * dx + dy * dy < 0.25) return draft;
    return { ...draft, points: [...draft.points, point] };
}

export function commandForDraft(draft: PaintDraft, color: string, size: number, filled: boolean): PaintCommand {
    const end = draft.points[draft.points.length - 1];
    if (draft.tool === "brush" || draft.tool === "eraser") {
        return {
            kind: "polyline",
            points: draft.points,
            // destinationOut uses only source alpha. Keep the eraser fully opaque and
            // independent of the selected paint color, including its optional alpha.
            stroke: draft.tool === "eraser" ? "#000000" : color,
            strokeThickness: size,
            lineCap: "round",
            lineJoin: "round",
            composite: draft.tool === "eraser" ? "destinationOut" : undefined,
        };
    }
    if (draft.tool === "line")
        return { kind: "line", x1: draft.start.x, y1: draft.start.y, x2: end.x, y2: end.y, stroke: color, strokeThickness: size };
    const x = Math.min(draft.start.x, end.x);
    const y = Math.min(draft.start.y, end.y);
    const width = Math.abs(end.x - draft.start.x);
    const height = Math.abs(end.y - draft.start.y);
    if (draft.tool === "rectangle")
        return { kind: "rectangle", x, y, width, height, fill: filled ? color : undefined, stroke: filled ? undefined : color, strokeThickness: size };
    return {
        kind: "ellipse",
        centerX: x + width / 2,
        centerY: y + height / 2,
        radiusX: width / 2,
        radiusY: height / 2,
        fill: filled ? color : undefined,
        stroke: filled ? undefined : color,
        strokeThickness: size,
    };
}

export function appendCommand(document: PaintDocument, layerId: string, command: PaintCommand): PaintDocument {
    return mapLayer(document, layerId, layer => ({ ...layer, commands: [...layer.commands, command] }));
}

export function addLayer(document: PaintDocument, afterLayerId?: string): { document: PaintDocument; layerId: string } {
    if (document.layers.length >= 64) throw new Error("SharpPaint supports at most 64 layers.");
    const id = createUniqueLayerId(document);
    const layer: PaintLayer = { id, name: "Layer " + (document.layers.length + 1), isVisible: true, opacity: 1, commands: [] };
    const index = afterLayerId === undefined ? document.layers.length : findLayerIndex(document, afterLayerId) + 1;
    return { document: { ...document, layers: [...document.layers.slice(0, index), layer, ...document.layers.slice(index)] }, layerId: id };
}

export function duplicateLayer(document: PaintDocument, layerId: string): { document: PaintDocument; layerId: string } {
    const index = findLayerIndex(document, layerId);
    const source = document.layers[index];
    const id = createUniqueLayerId(document);
    const copy: PaintLayer = { ...source, id, name: source.name + " copy", commands: source.commands.slice() };
    return { document: { ...document, layers: [...document.layers.slice(0, index + 1), copy, ...document.layers.slice(index + 1)] }, layerId: id };
}

export function deleteLayer(document: PaintDocument, layerId: string): { document: PaintDocument; layerId: string } {
    const index = findLayerIndex(document, layerId);
    if (document.layers.length === 1) {
        const replacement = addLayer({ ...document, layers: [] });
        return replacement;
    }
    const layers = [...document.layers.slice(0, index), ...document.layers.slice(index + 1)];
    return { document: { ...document, layers }, layerId: layers[Math.min(index, layers.length - 1)].id };
}

export function moveLayer(document: PaintDocument, layerId: string, direction: -1 | 1): PaintDocument {
    const index = findLayerIndex(document, layerId);
    const target = index + direction;
    if (target < 0 || target >= document.layers.length) return document;
    const layers = document.layers.slice();
    const temporary = layers[index];
    layers[index] = layers[target];
    layers[target] = temporary;
    return { ...document, layers };
}

export function renameLayer(document: PaintDocument, layerId: string, name: string): PaintDocument {
    const normalized = name.trim().slice(0, 80);
    return mapLayer(document, layerId, layer => ({ ...layer, name: normalized === "" ? "Untitled layer" : normalized }));
}
export function setLayerVisibility(document: PaintDocument, layerId: string, isVisible: boolean): PaintDocument {
    return mapLayer(document, layerId, layer => ({ ...layer, isVisible }));
}
export function setLayerOpacity(document: PaintDocument, layerId: string, opacity: number): PaintDocument {
    return mapLayer(document, layerId, layer => ({ ...layer, opacity: Math.max(0, Math.min(1, opacity)) }));
}

export function serializeProject(document: PaintDocument): string {
    validateDocument(document);
    return JSON.stringify(document, null, 2);
}

export function parseProject(json: string): PaintDocument {
    if (json.length > 100 * 1024 * 1024) throw new Error("SharpPaint projects are limited to 100 MiB.");
    const value: any = JSON.parse(json);
    validateDocument(value);
    return value as PaintDocument;
}

export function validateDocument(value: any): void {
    if (value === null || typeof value !== "object" || value.format !== "sharpaint" || value.version !== 1)
        throw new Error("This is not a supported SharpPaint v1 project.");
    validateDimension(value.width, "width");
    validateDimension(value.height, "height");
    if (!Array.isArray(value.layers) || value.layers.length < 1 || value.layers.length > 64)
        throw new Error("A SharpPaint project requires between 1 and 64 layers.");
    const ids: string[] = [];
    let commandCount = 0;
    for (const layer of value.layers) {
        if (layer === null || typeof layer !== "object" || typeof layer.id !== "string" || layer.id === "" || ids.indexOf(layer.id) >= 0)
            throw new Error("Every layer requires a unique non-empty ID.");
        ids.push(layer.id);
        if (typeof layer.name !== "string" || layer.name.length > 80 || typeof layer.isVisible !== "boolean" || !finite(layer.opacity) || layer.opacity < 0 || layer.opacity > 1)
            throw new Error("A layer contains invalid metadata.");
        if (!Array.isArray(layer.commands)) throw new Error("Every layer requires a command list.");
        commandCount += layer.commands.length;
        if (commandCount > 100000) throw new Error("SharpPaint projects are limited to 100,000 drawing commands.");
        for (const command of layer.commands) validateCommand(command);
    }
}

export function validColor(value: string): boolean {
    return /^#[0-9a-fA-F]{6}$/.test(value) || /^#[0-9a-fA-F]{8}$/.test(value);
}

function validateDimension(value: number, name: string): void {
    if (!finite(value) || value < 1 || value > 8192 || Math.floor(value) !== value)
        throw new Error("Document " + name + " must be an integer from 1 to 8192.");
}
function finite(value: any): boolean { return typeof value === "number" && Number.isFinite(value); }
function validateCommand(command: any): void {
    if (command === null || typeof command !== "object" || ["polyline", "line", "rectangle", "ellipse", "image"].indexOf(command.kind) < 0)
        throw new Error("A layer contains an unsupported drawing command.");
    const numbers: number[] = [];
    if (command.kind === "polyline") {
        if (!Array.isArray(command.points) || command.points.length < 1 || command.points.length > 10000)
            throw new Error("A polyline requires between 1 and 10,000 points.");
        for (const point of command.points) {
            if (point === null || typeof point !== "object") throw new Error("Every polyline point requires finite x/y coordinates.");
            numbers.push(point.x);
            numbers.push(point.y);
        }
    } else if (command.kind === "line") numbers.push(command.x1, command.y1, command.x2, command.y2);
    else if (command.kind === "ellipse") numbers.push(command.centerX, command.centerY, command.radiusX, command.radiusY);
    else numbers.push(command.x, command.y, command.width, command.height);
    for (const number of numbers) if (!finite(number)) throw new Error("Drawing coordinates must be finite.");
    if (command.kind === "image") {
        if (typeof command.source !== "string" || !command.source.startsWith("data:image/png;base64,") || command.source.length > 35_000_000)
            throw new Error("Saved image layers must contain embedded PNG data.");
        if (command.width <= 0 || command.height <= 0) throw new Error("Image dimensions must be positive.");
    } else {
        if (!finite(command.strokeThickness) || command.strokeThickness <= 0 || command.strokeThickness > 256)
            throw new Error("Stroke thickness is invalid.");
        if (command.stroke !== undefined && !validColor(command.stroke)) throw new Error("Stroke color is invalid.");
        if (command.fill !== undefined && !validColor(command.fill)) throw new Error("Fill color is invalid.");
        if (command.stroke === undefined && command.fill === undefined) throw new Error("A drawing command requires a stroke or fill color.");
        if (command.kind === "polyline" && (command.lineCap !== "round" || command.lineJoin !== "round"))
            throw new Error("SharpPaint polylines require round caps and joins.");
        if (command.composite !== undefined && command.composite !== "destinationOut")
            throw new Error("Drawing compositing mode is invalid.");
    }
}
function findLayerIndex(document: PaintDocument, layerId: string): number {
    const index = document.layers.findIndex(layer => layer.id === layerId);
    if (index < 0) throw new Error("The selected layer no longer exists.");
    return index;
}
function createUniqueLayerId(document: PaintDocument): string {
    let id = createLayerId();
    while (document.layers.findIndex(layer => layer.id === id) >= 0) id = createLayerId();
    return id;
}
function mapLayer(document: PaintDocument, layerId: string, update: (layer: PaintLayer) => PaintLayer): PaintDocument {
    const index = findLayerIndex(document, layerId);
    return { ...document, layers: document.layers.map((layer, current) => current === index ? update(layer) : layer) };
}
