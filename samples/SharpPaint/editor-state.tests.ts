import { appReducer, initialState } from "./editor-state";
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
