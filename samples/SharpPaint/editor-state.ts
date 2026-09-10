import type { DrawingEffect } from "@sharpts/gui";
import {
    PaintCommand,
    DocumentHistory,
    PaintDocument,
    PaintDraft,
    PaintLayer,
    PaintTool,
    DrawingTool,
    addLayer,
    appendCommand,
    beginDraft,
    clampPoint,
    commandForDraft,
    commitDocument,
    createDocument,
    createHistory,
    createImportedDocument,
    createTextCommand,
    deleteLayer,
    duplicateLayer,
    extendDraft,
    markSaved,
    moveLayer,
    parseProject,
    replaceLayerCommands,
    redo,
    renameLayer,
    serializeProject,
    setLayerOpacity,
    setLayerVisibility,
    undo,
    validColor
} from "./document";

export const DEFAULT_COLOR = "#111827";
export const COLORS = [
    DEFAULT_COLOR,
    "#ffffff",
    "#ef4444",
    "#f97316",
    "#f59e0b",
    "#eab308",
    "#22c55e",
    "#14b8a6",
    "#06b6d4",
    "#3b82f6",
    "#6366f1",
    "#8b5cf6",
    "#d946ef",
    "#ec4899",
    "#78716c",
    "#94a3b8"
];
export const FONT_FAMILIES = ["sans-serif", "serif", "monospace"];

export interface TextDraft {
    readonly start: { readonly x: number; readonly y: number };
    readonly x: number;
    readonly y: number;
    readonly width: number;
    readonly height: number;
    readonly text: string;
    readonly editing: boolean;
    readonly commandIndex: number;
    readonly layerId: string;
}

export type EffectDialogKind = "gaussianBlur" | "brightnessContrast" | "hueSaturation";
export interface EffectDialogState {
    readonly kind: EffectDialogKind;
    readonly first: number;
    readonly second: number;
}
export interface RasterPreview {
    readonly layerId: string;
    readonly revision: number;
    readonly signature: string;
    readonly command: {
        readonly kind: "image";
        readonly source: string;
        readonly x: number;
        readonly y: number;
        readonly width: number;
        readonly height: number;
    };
}

export interface AppState {
    readonly history: DocumentHistory;
    readonly selectedLayerId: string;
    readonly tool: PaintTool;
    readonly color: string;
    readonly backgroundColor: string;
    readonly recentColors: readonly string[];
    readonly size: number;
    readonly filled: boolean;
    readonly zoom: number;
    readonly zoomMode: "fit" | "manual";
    readonly documentVersion: number;
    readonly draft: PaintDraft | null;
    readonly draftColor: string;
    readonly draftSize: number;
    readonly draftFilled: boolean;
    readonly cursor: { readonly x: number; readonly y: number } | null;
    readonly filePath: string | null;
    readonly status: string;
    readonly windowWidth: number;
    readonly windowHeight: number;
    readonly layersPaneOpen: boolean;
    readonly revision: number;
    readonly fillTolerance: number;
    readonly fontFamily: string;
    readonly textSize: number;
    readonly textBold: boolean;
    readonly textItalic: boolean;
    readonly textDraft: TextDraft | null;
    readonly effectDialog: EffectDialogState | null;
    readonly effectPreview: RasterPreview | null;
    readonly busy: string | null;
    readonly opacityStart: PaintDocument | null;
    readonly theme: "light" | "dark";
}

export type AppAction =
    | { type: "tool"; tool: PaintTool }
    | { type: "color"; color: string }
    | { type: "size"; size: number }
    | { type: "filled"; filled: boolean }
    | { type: "zoom"; zoom: number; automatic?: boolean }
    | { type: "fit" }
    | { type: "finishText" }
    | { type: "backgroundColor"; color: string }
    | { type: "swapColors" }
    | { type: "pointerDown"; point: { x: number; y: number } }
    | { type: "pointerMove"; point: { x: number; y: number } }
    | { type: "pointerUp"; point: { x: number; y: number } }
    | { type: "pointerCancel" }
    | { type: "undo" }
    | { type: "redo" }
    | { type: "selectLayer"; layerId: string }
    | { type: "addLayer" }
    | { type: "reorderLayer"; layerId: string; index: number }
    | { type: "duplicateLayer" }
    | { type: "deleteLayer" }
    | { type: "moveLayer"; direction: "up" | "down" }
    | { type: "renameLayer"; name: string; layerId?: string }
    | { type: "visibility"; layerId: string; value: boolean }
    | { type: "opacity"; value: number }
    | { type: "beginOpacity" }
    | { type: "endOpacity" }
    | { type: "theme"; theme: "light" | "dark" }
    | { type: "load"; document: PaintDocument; filePath: string | null; status: string; recovered?: boolean }
    | { type: "saved"; filePath: string; status: string; document: PaintDocument }
    | { type: "status"; status: string }
    | { type: "metrics"; width: number; height: number }
    | { type: "toggleLayers"; value: boolean }
    | { type: "fillTolerance"; value: number }
    | { type: "fontFamily"; value: string }
    | { type: "textSize"; value: number }
    | { type: "textBold"; value: boolean }
    | { type: "textItalic"; value: boolean }
    | { type: "textStart"; point: { x: number; y: number } }
    | { type: "textMove"; point: { x: number; y: number } }
    | { type: "textEdit"; point: { x: number; y: number } }
    | { type: "textValue"; value: string }
    | { type: "cancelText" }
    | { type: "showEffect"; kind: EffectDialogKind }
    | { type: "effectParameter"; first?: number; second?: number }
    | { type: "effectPreview"; preview: RasterPreview }
    | { type: "cancelEffect" }
    | { type: "busy"; value: string | null }
    | { type: "replaceLayer"; layerId: string; source: string; status: string; expectedRevision: number }
    | { type: "mergeLayer"; source: string; expectedRevision: number }
    | { type: "commitText"; command: PaintCommand };

export function initialState(document: PaintDocument = createDocument()): AppState {
    return {
        history: createHistory(document),
        selectedLayerId: document.layers[0].id,
        tool: "brush",
        color: DEFAULT_COLOR,
        backgroundColor: "#ffffff",
        recentColors: [],
        size: 8,
        filled: false,
        zoom: 0.75,
        zoomMode: "fit",
        documentVersion: 0,
        draft: null,
        draftColor: DEFAULT_COLOR,
        draftSize: 8,
        draftFilled: false,
        cursor: null,
        filePath: null,
        status: "Ready · Draw with the Brush tool",
        windowWidth: 1120,
        windowHeight: 700,
        layersPaneOpen: false,
        revision: 0,
        fillTolerance: 0.08,
        fontFamily: FONT_FAMILIES[0],
        textSize: 32,
        textBold: false,
        textItalic: false,
        textDraft: null,
        effectDialog: null,
        effectPreview: null,
        busy: null,
        opacityStart: null,
        theme: "light"
    };
}

function updateDocument(
    state: AppState,
    document: PaintDocument,
    selectedLayerId: string = state.selectedLayerId,
    label: string = "Document change"
): AppState {
    if (document === state.history.document) return state;
    return {
        ...state,
        history: commitDocument(state.history, document, label),
        selectedLayerId: validSelection(document, selectedLayerId),
        revision: state.revision + 1,
        draft: null,
        textDraft: null,
        effectDialog: null,
        effectPreview: null
    };
}

function resizeTextDraft(draft: TextDraft, point: { readonly x: number; readonly y: number }): TextDraft {
    return {
        ...draft,
        x: Math.min(draft.start.x, point.x),
        y: Math.min(draft.start.y, point.y),
        width: Math.abs(point.x - draft.start.x),
        height: Math.abs(point.y - draft.start.y)
    };
}

export function effectTitle(kind: EffectDialogKind): string {
    if (kind === "gaussianBlur") return "Gaussian blur";
    if (kind === "brightnessContrast") return "Brightness / contrast";
    return "Hue / saturation";
}

export function effectForDialog(dialog: EffectDialogState): DrawingEffect {
    if (dialog.kind === "gaussianBlur") return { kind: "gaussianBlur", radius: dialog.first };
    if (dialog.kind === "brightnessContrast")
        return { kind: "brightnessContrast", brightness: dialog.first, contrast: dialog.second };
    return { kind: "hueSaturation", hue: dialog.first, saturation: dialog.second };
}

export function effectName(effect: DrawingEffect): string {
    if (effect.kind === "gaussianBlur") return "Gaussian blur";
    if (effect.kind === "brightnessContrast") return "Brightness / contrast";
    if (effect.kind === "hueSaturation") return "Hue / saturation";
    if (effect.kind === "grayscale") return "Grayscale";
    return "Invert";
}

function requireDrawingTool(tool: PaintTool): DrawingTool {
    if (tool === "brush") return "brush";
    if (tool === "eraser") return "eraser";
    if (tool === "line") return "line";
    if (tool === "rectangle") return "rectangle";
    if (tool === "ellipse") return "ellipse";
    throw new Error("The selected tool does not create a drawing gesture.");
}

function finishOpacity(state: AppState): AppState {
    if (state.opacityStart === null) return state;
    const history = commitDocument(
        { ...state.history, document: state.opacityStart },
        state.history.document,
        "Change opacity"
    );
    return { ...state, history, opacityStart: null };
}

export function finishText(state: AppState): AppState {
    const draft = state.textDraft;
    if (!draft || !draft.editing) return state;
    const layer = state.history.document.layers.find((layer) => layer.id === draft.layerId);
    if (!layer) return { ...state, textDraft: null };
    if (!draft.text) {
        if (draft.commandIndex < 0) return { ...state, textDraft: null };
        return {
            ...updateDocument(
                state,
                replaceLayerCommands(
                    state.history.document,
                    layer.id,
                    layer.commands.filter((_, index) => index !== draft.commandIndex)
                ),
                state.selectedLayerId,
                "Delete text"
            ),
            textDraft: null
        };
    }
    const command = createTextCommand(
        draft.text,
        draft,
        state.color,
        state.fontFamily,
        state.textSize,
        state.textBold,
        state.textItalic
    );
    if (
        draft.commandIndex >= 0 &&
        JSON.stringify(layer.commands[draft.commandIndex]) === JSON.stringify(command)
    )
        return { ...state, textDraft: null };
    const document =
        draft.commandIndex < 0
            ? appendCommand(state.history.document, layer.id, command)
            : replaceLayerCommands(
                  state.history.document,
                  layer.id,
                  layer.commands.map((value, index) => (index === draft.commandIndex ? command : value))
              );
    return {
        ...updateDocument(
            state,
            document,
            state.selectedLayerId,
            draft.commandIndex < 0 ? "Add text" : "Edit text"
        ),
        textDraft: null,
        status: "Text committed"
    };
}

export function appReducer(state: AppState, action: AppAction): AppState {
    if (
        (
            [
                "tool",
                "selectLayer",
                "addLayer",
                "duplicateLayer",
                "deleteLayer",
                "moveLayer",
                "reorderLayer",
                "visibility",
                "showEffect",
                "textStart",
                "undo",
                "redo",
                "beginOpacity",
                "renameLayer"
            ] as string[]
        ).indexOf(action.type) >= 0
    )
        state = finishText(finishOpacity(state));
    switch (action.type) {
        case "finishText":
            return finishText(finishOpacity(state));
        case "fit":
            return { ...state, zoomMode: "fit" };
        case "backgroundColor":
            return validColor(action.color)
                ? { ...state, backgroundColor: action.color.toLowerCase() }
                : state;
        case "swapColors":
            return {
                ...state,
                color: state.backgroundColor,
                backgroundColor: state.color,
                status: "Colors swapped"
            };
        case "tool": {
            const tool = action.tool;
            return {
                ...state,
                tool,
                draft: null,
                textDraft: null,
                effectDialog: null,
                effectPreview: null,
                status: toolLabel(tool) + " selected"
            };
        }
        case "color": {
            let color = action.color.trim().toLowerCase();
            if (color.length === 9 && color.slice(1, 3) === "ff") color = "#" + color.slice(3);
            return validColor(color)
                ? {
                      ...state,
                      color,
                      recentColors: [color, ...state.recentColors.filter((value) => value !== color)].slice(
                          0,
                          6
                      ),
                      status: "Color " + color.toUpperCase()
                  }
                : state;
        }
        case "size":
            return { ...state, size: Math.round(Math.max(1, Math.min(64, action.size))) };
        case "filled":
            return { ...state, filled: action.filled };
        case "zoom":
            return {
                ...state,
                zoom: Math.max(0.01, Math.min(8, action.zoom)),
                zoomMode: action.automatic ? state.zoomMode : "manual"
            };
        case "pointerDown": {
            const point = clampPoint(state.history.document, action.point);
            return {
                ...state,
                draft: beginDraft(requireDrawingTool(state.tool), point),
                draftColor: state.color,
                draftSize: state.size,
                draftFilled: state.filled,
                cursor: point,
                status: "Drawing " + toolLabel(state.tool).toLowerCase()
            };
        }
        case "pointerMove": {
            const point = clampPoint(state.history.document, action.point);
            return {
                ...state,
                cursor: point,
                draft: state.draft === null ? null : extendDraft(state.draft, point)
            };
        }
        case "pointerUp": {
            if (state.draft === null) return state;
            const pointerPoint = action.point;
            const clampedPoint = clampPoint(state.history.document, pointerPoint);
            const draft = extendDraft(state.draft, clampedPoint);
            const command = commandForDraft(draft, state.draftColor, state.draftSize, state.draftFilled);
            const document = appendCommand(state.history.document, state.selectedLayerId, command);
            return {
                ...updateDocument(state, document, state.selectedLayerId, toolLabel(state.tool)),
                draft: null,
                cursor: clampedPoint,
                status: toolLabel(state.tool) + " committed"
            };
        }
        case "pointerCancel":
            return { ...state, draft: null, status: "Gesture canceled" };
        case "undo": {
            const history = undo(state.history);
            return {
                ...state,
                history,
                opacityStart: null,
                selectedLayerId: validSelection(history.document, state.selectedLayerId),
                revision: state.revision + 1,
                draft: null,
                textDraft: null,
                effectDialog: null,
                effectPreview: null,
                status:
                    state.history.past.length === 0
                        ? "Nothing to undo"
                        : "Undid " + state.history.pastLabels[state.history.pastLabels.length - 1]
            };
        }
        case "redo": {
            const history = redo(state.history);
            return {
                ...state,
                history,
                opacityStart: null,
                selectedLayerId: validSelection(history.document, state.selectedLayerId),
                revision: state.revision + 1,
                draft: null,
                textDraft: null,
                effectDialog: null,
                effectPreview: null,
                status:
                    state.history.future.length === 0
                        ? "Nothing to redo"
                        : "Redid " + state.history.futureLabels[0]
            };
        }
        case "selectLayer":
            return {
                ...state,
                selectedLayerId: validSelection(state.history.document, action.layerId),
                draft: null,
                textDraft: null
            };
        case "addLayer": {
            const result = addLayer(state.history.document, state.selectedLayerId);
            return {
                ...updateDocument(state, result.document, result.layerId, "Add layer"),
                status: "Added layer"
            };
        }
        case "duplicateLayer": {
            const result = duplicateLayer(state.history.document, state.selectedLayerId);
            return {
                ...updateDocument(state, result.document, result.layerId, "Duplicate layer"),
                status: "Duplicated layer"
            };
        }
        case "deleteLayer": {
            const result = deleteLayer(state.history.document, state.selectedLayerId);
            return {
                ...updateDocument(state, result.document, result.layerId, "Delete layer"),
                status: "Deleted layer"
            };
        }
        case "reorderLayer": {
            const document = state.history.document;
            const index = document.layers.findIndex((layer) => layer.id === action.layerId);
            const target = Math.max(0, Math.min(document.layers.length - 1, action.index));
            if (index < 0 || index === target) return state;
            const layers = document.layers.slice();
            const layer = layers.splice(index, 1)[0];
            layers.splice(target, 0, layer);
            return updateDocument(state, { ...document, layers }, layer.id, "Reorder layer");
        }
        case "moveLayer": {
            const direction = (action.direction === "up" ? 1 : -1) as -1 | 1;
            return {
                ...updateDocument(
                    state,
                    moveLayer(state.history.document, state.selectedLayerId, direction),
                    state.selectedLayerId,
                    direction > 0 ? "Raise layer" : "Lower layer"
                ),
                status: direction > 0 ? "Raised layer" : "Lowered layer"
            };
        }
        case "renameLayer":
            return updateDocument(
                state,
                renameLayer(state.history.document, action.layerId || state.selectedLayerId, action.name),
                state.selectedLayerId,
                "Rename layer"
            );
        case "visibility":
            return updateDocument(
                state,
                setLayerVisibility(state.history.document, action.layerId, action.value),
                state.selectedLayerId,
                "Layer visibility"
            );
        case "theme":
            return { ...state, theme: action.theme };
        case "beginOpacity":
            return state.opacityStart === null ? { ...state, opacityStart: state.history.document } : state;
        case "endOpacity":
            return finishOpacity(state);
        case "opacity": {
            const document = setLayerOpacity(state.history.document, state.selectedLayerId, action.value);
            if (state.opacityStart === null)
                return updateDocument(state, document, state.selectedLayerId, "Change opacity");
            return {
                ...state,
                history: { ...state.history, document, dirty: document !== state.history.savedDocument },
                revision: state.revision + 1
            };
        }
        case "load": {
            const loaded = action.document;
            return {
                ...state,
                history: action.recovered
                    ? { ...createHistory(loaded), savedDocument: null, dirty: true }
                    : createHistory(loaded),
                selectedLayerId: loaded.layers[loaded.layers.length - 1].id,
                filePath: action.filePath,
                zoomMode: "fit",
                documentVersion: state.documentVersion + 1,
                revision: state.revision + 1,
                draft: null,
                textDraft: null,
                effectDialog: null,
                effectPreview: null,
                busy: null,
                status: action.status,
                opacityStart: null
            };
        }
        case "saved":
            return {
                ...state,
                history: markSaved(state.history, action.document),
                filePath: action.filePath,
                status: action.status
            };
        case "status":
            return { ...state, status: action.status };
        case "metrics": {
            const windowWidth = Math.max(1, Math.round(action.width));
            const windowHeight = Math.max(1, Math.round(action.height));
            if (windowWidth === state.windowWidth && windowHeight === state.windowHeight) return state;
            return { ...state, windowWidth, windowHeight };
        }
        case "toggleLayers":
            return { ...state, layersPaneOpen: action.value };
        case "fillTolerance":
            return { ...state, fillTolerance: Math.max(0, Math.min(1, action.value)) };
        case "fontFamily":
            return FONT_FAMILIES.indexOf(action.value) < 0 ? state : { ...state, fontFamily: action.value };
        case "textSize":
            return { ...state, textSize: Math.round(Math.max(6, Math.min(144, action.value))) };
        case "textBold":
            return { ...state, textBold: action.value };
        case "textItalic":
            return { ...state, textItalic: action.value };
        case "textStart": {
            const layer = state.history.document.layers.find((layer) => layer.id === state.selectedLayerId)!;
            for (let index = layer.commands.length - 1; index >= 0; index--) {
                const command = layer.commands[index];
                if (
                    command.kind === "text" &&
                    action.point.x >= command.x &&
                    action.point.y >= command.y &&
                    action.point.x <= command.x + command.width &&
                    action.point.y <= command.y + command.height
                ) {
                    return {
                        ...state,
                        color: command.fill,
                        fontFamily: command.fontFamily,
                        textSize: command.fontSize,
                        textBold: command.fontWeight === "bold",
                        textItalic: command.fontStyle === "italic",
                        textDraft: {
                            start: { x: command.x, y: command.y },
                            x: command.x,
                            y: command.y,
                            width: command.width,
                            height: command.height,
                            text: command.text,
                            editing: true,
                            layerId: layer.id,
                            commandIndex: index
                        },
                        status: "Editing text · Ctrl+Enter to apply"
                    };
                }
            }
            const point = clampPoint(state.history.document, action.point);
            return {
                ...state,
                draft: null,
                textDraft: {
                    start: point,
                    x: point.x,
                    y: point.y,
                    width: 1,
                    height: 1,
                    text: "",
                    editing: false,
                    layerId: layer.id,
                    commandIndex: -1
                },
                cursor: point,
                status: "Drag to create a text box"
            };
        }
        case "textMove": {
            const textDraft = state.textDraft;
            if (textDraft === null || textDraft.editing) return state;
            const point = clampPoint(state.history.document, action.point);
            return { ...state, cursor: point, textDraft: resizeTextDraft(textDraft, point) };
        }
        case "textEdit": {
            const textDraft = state.textDraft;
            if (textDraft === null || textDraft.editing) return state;
            const point = clampPoint(state.history.document, action.point);
            const resized = resizeTextDraft(textDraft, point);
            const width =
                resized.width < 8 ? Math.min(280, state.history.document.width - resized.x) : resized.width;
            const height =
                resized.height < 8
                    ? Math.min(120, state.history.document.height - resized.y)
                    : resized.height;
            return {
                ...state,
                cursor: point,
                textDraft: {
                    ...resized,
                    width: Math.max(1, width),
                    height: Math.max(1, height),
                    editing: true
                },
                status: "Enter text, then apply or press Ctrl+Enter"
            };
        }
        case "textValue":
            return state.textDraft === null
                ? state
                : {
                      ...state,
                      textDraft: { ...state.textDraft, text: action.value.slice(0, 65_536) }
                  };
        case "cancelText":
            return { ...state, textDraft: null, status: "Text canceled" };
        case "showEffect": {
            const kind = action.kind;
            const dialog: EffectDialogState =
                kind === "gaussianBlur" ? { kind, first: 4, second: 0 } : { kind, first: 0, second: 0 };
            return {
                ...state,
                effectDialog: dialog,
                effectPreview: null,
                textDraft: null,
                draft: null,
                status: effectTitle(kind) + " settings"
            };
        }
        case "effectParameter": {
            if (state.effectDialog === null) return state;
            return {
                ...state,
                effectDialog: {
                    ...state.effectDialog,
                    first: action.first === undefined ? state.effectDialog.first : action.first,
                    second: action.second === undefined ? state.effectDialog.second : action.second
                },
                effectPreview: null
            };
        }
        case "effectPreview": {
            const preview = action.preview;
            return preview.revision === state.revision
                ? { ...state, effectPreview: preview, busy: null, status: "Effect preview ready" }
                : { ...state, busy: null, status: "Discarded a stale effect preview" };
        }
        case "cancelEffect":
            return {
                ...state,
                effectDialog: null,
                effectPreview: null,
                busy: null,
                status: "Effect canceled"
            };
        case "busy":
            return {
                ...state,
                busy: action.value,
                status: action.value === null ? state.status : action.value
            };
        case "replaceLayer": {
            if (
                action.expectedRevision !== state.revision ||
                state.history.document.layers.findIndex((layer) => layer.id === action.layerId) < 0
            )
                return { ...state, busy: null, status: "Discarded a stale graphics result" };
            const document = replaceLayerCommands(state.history.document, action.layerId, [
                {
                    kind: "image",
                    source: action.source,
                    x: 0,
                    y: 0,
                    width: state.history.document.width,
                    height: state.history.document.height
                }
            ]);
            return {
                ...updateDocument(state, document, state.selectedLayerId, action.status),
                busy: null,
                status: action.status
            };
        }
        case "mergeLayer": {
            if (action.expectedRevision !== state.revision) return { ...state, busy: null };
            const document = state.history.document;
            const index = document.layers.findIndex((layer) => layer.id === state.selectedLayerId);
            if (index <= 0) return state;
            const below = document.layers[index - 1];
            const merged: PaintLayer = {
                ...below,
                opacity: 1,
                isVisible: true,
                commands: [
                    {
                        kind: "image",
                        source: action.source,
                        x: 0,
                        y: 0,
                        width: document.width,
                        height: document.height
                    }
                ]
            };
            const layers = document.layers
                .filter((layer) => layer.id !== state.selectedLayerId)
                .map((layer) => (layer.id === below.id ? merged : layer));
            return {
                ...updateDocument(state, { ...document, layers }, below.id, "Merge layers"),
                busy: null,
                status: "Merged layers"
            };
        }
        case "commitText": {
            const layer = state.history.document.layers.find((layer) => layer.id === state.selectedLayerId)!;
            const index = state.textDraft === null ? -1 : state.textDraft.commandIndex;
            const document =
                index < 0
                    ? appendCommand(state.history.document, state.selectedLayerId, action.command)
                    : replaceLayerCommands(
                          state.history.document,
                          state.selectedLayerId,
                          layer.commands.map((command, position) =>
                              position === index ? action.command : command
                          )
                      );
            return {
                ...updateDocument(
                    state,
                    document,
                    state.selectedLayerId,
                    index < 0 ? "Add text" : "Edit text"
                ),
                status: "Text committed"
            };
        }
    }
    return state;
}

export function toolLabel(tool: PaintTool): string {
    switch (tool) {
        case "brush":
            return "Brush";
        case "eraser":
            return "Eraser";
        case "line":
            return "Line";
        case "rectangle":
            return "Rectangle";
        case "ellipse":
            return "Ellipse";
        case "fill":
            return "Fill";
        case "picker":
            return "Picker";
        case "text":
            return "Text";
    }
    return "Tool";
}

export function validSelection(document: PaintDocument, id: string): string {
    return document.layers.some((layer) => layer.id === id)
        ? id
        : document.layers[document.layers.length - 1].id;
}
