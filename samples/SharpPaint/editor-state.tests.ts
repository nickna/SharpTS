import { appReducer, effectForDialog, initialState } from "./editor-state";
import type { RasterPreview } from "./editor-state";
import { createDocument } from "./document";
function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("Editor state: " + name);
}
let editor = initialState(createDocument(320, 240));
const originalLayer = editor.selectedLayerId;
editor = appReducer(editor, { type: "tool", tool: "text" });
editor = appReducer(editor, { type: "textStart", point: { x: 10, y: 10 } });
editor = appReducer(editor, { type: "textEdit", point: { x: 210, y: 100 } });
editor = appReducer(editor, { type: "textValue", value: "Keep my text" });
editor = appReducer(editor, { type: "addLayer" });
expect(
    "text is committed to its original layer before adding a layer",
    editor.history.document.layers[0].commands.length === 2 &&
        editor.selectedLayerId !== originalLayer &&
        editor.textDraft === null
);
editor = appReducer(editor, { type: "undo" });
expect(
    "undo layer addition retains committed text",
    editor.history.document.layers.length === 1 && editor.history.document.layers[0].commands.length === 2
);
editor = appReducer(editor, { type: "undo" });
expect("undo committed text", editor.history.document.layers[0].commands.length === 1);
editor = appReducer(editor, { type: "color", color: "#FFEF4444" });
editor = appReducer(editor, { type: "color", color: "#ef4444" });
expect(
    "equivalent colors have one recent entry",
    editor.color === "#ef4444" && editor.recentColors.length === 1
);
editor = appReducer(editor, { type: "addLayer" });
const upperLayer = editor.selectedLayerId;
editor = appReducer(editor, { type: "reorderLayer", layerId: upperLayer, index: 0 });
expect("reorder changes stacking", editor.history.document.layers[0].id === upperLayer);
editor = appReducer(editor, { type: "undo" });
expect("reorder is undoable", editor.history.document.layers[1].id === upperLayer);
editor = appReducer(editor, { type: "zoom", zoom: 2 });
expect("explicit zoom leaves fit mode", editor.zoomMode === "manual");
editor = appReducer(editor, { type: "fit" });
editor = appReducer(editor, { type: "zoom", zoom: 0.5, automatic: true });
expect("automatic zoom keeps fit mode", editor.zoomMode === "fit");

let opacityEditor = initialState(createDocument(40, 40));
opacityEditor = appReducer(opacityEditor, { type: "beginOpacity" });
opacityEditor = appReducer(opacityEditor, { type: "opacity", value: 0.4 });
opacityEditor = appReducer(opacityEditor, { type: "addLayer" });
opacityEditor = appReducer(opacityEditor, { type: "undo" });
expect(
    "changing context ends the opacity transaction",
    opacityEditor.history.document.layers.length === 1 &&
        opacityEditor.history.document.layers[0].opacity === 0.4
);
opacityEditor = appReducer(opacityEditor, { type: "undo" });
expect("opacity remains its own undo step", opacityEditor.history.document.layers[0].opacity === 1);

let effectEditor = appReducer(initialState(createDocument(40, 40)), { type: "showEffect", kind: "gaussianBlur" });
const radiusFourPreview: RasterPreview = {
    revision: effectEditor.revision,
    layerId: effectEditor.selectedLayerId,
    signature: JSON.stringify(effectForDialog(effectEditor.effectDialog!)),
    command: { kind: "image", source: "radius-four", x: 0, y: 0, width: 40, height: 40 }
};
effectEditor = appReducer(effectEditor, { type: "effectPreview", preview: radiusFourPreview });
expect("initial matching preview is ready", effectEditor.effectPreview === radiusFourPreview && effectEditor.status === "Effect preview ready");
effectEditor = appReducer(effectEditor, { type: "effectParameter", first: 8 });
expect("parameter edit invalidates ready status immediately", effectEditor.effectPreview === null && effectEditor.status === "Effect preview pending");
effectEditor = appReducer(effectEditor, { type: "busy", value: "Rendering effect preview…" });
const pendingEffect = effectEditor;
effectEditor = appReducer(effectEditor, { type: "effectPreview", preview: radiusFourPreview });
expect("old radius completion cannot finish the replacement", effectEditor === pendingEffect);
const radiusEightPreview: RasterPreview = {
    ...radiusFourPreview,
    signature: JSON.stringify(effectForDialog(effectEditor.effectDialog!)),
    command: { ...radiusFourPreview.command, source: "radius-eight" }
};
effectEditor = appReducer(effectEditor, { type: "effectPreview", preview: { ...radiusEightPreview, layerId: "another-layer" } });
expect("another layer cannot finish the replacement", effectEditor === pendingEffect);
effectEditor = appReducer(effectEditor, { type: "effectPreview", preview: { ...radiusEightPreview, revision: effectEditor.revision - 1 } });
expect("another revision cannot finish the replacement", effectEditor === pendingEffect);
effectEditor = appReducer(effectEditor, { type: "effectPreview", preview: radiusEightPreview });
expect("current radius result finishes the replacement", effectEditor.effectPreview === radiusEightPreview && effectEditor.status === "Effect preview ready" && effectEditor.busy === null);
effectEditor = appReducer(effectEditor, { type: "cancelEffect" });
const canceledEffect = effectEditor;
effectEditor = appReducer(effectEditor, { type: "effectPreview", preview: radiusEightPreview });
expect("completion cannot reopen a canceled effect", effectEditor === canceledEffect);
