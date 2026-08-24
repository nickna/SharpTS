import {
    PaintDocument,
    addLayer,
    appendCommand,
    beginDraft,
    clampPoint,
    commandForDraft,
    commitDocument,
    createDocument,
    createHistory,
    createImportedDocument,
    deleteLayer,
    duplicateLayer,
    extendDraft,
    markSaved,
    moveLayer,
    parseProject,
    redo,
    renameLayer,
    serializeProject,
    setLayerOpacity,
    setLayerVisibility,
    undo,
} from "./document";

function expect(name: string, condition: boolean): void {
    if (!condition) throw new Error("SharpPaint model assertion failed: " + name);
}
function rejects(name: string, json: string): void {
    let rejected = false;
    try { parseProject(json); } catch (_) { rejected = true; }
    expect(name, rejected);
}

const initial = createDocument(320, 200);
expect("document dimensions", initial.width === 320 && initial.height === 200);
expect("white background", initial.layers[0].commands[0].kind === "rectangle");

const click = commandForDraft(beginDraft("brush", { x: 4, y: 7 }), "#112233", 5, false);
expect("click-only brush polyline", click.kind === "polyline" && click.points.length === 1);
const erased = commandForDraft(extendDraft(beginDraft("eraser", { x: 1, y: 2 }), { x: 9, y: 10 }), "#ffffff", 12, false);
expect("eraser compositing", erased.kind === "polyline" && erased.composite === "destinationOut");
const rectangle = commandForDraft(extendDraft(beginDraft("rectangle", { x: 20, y: 30 }), { x: 4, y: 8 }), "#abcdef", 3, true);
expect("normalized filled rectangle", rectangle.kind === "rectangle" && rectangle.x === 4 && rectangle.y === 8 && rectangle.width === 16 && rectangle.height === 22 && rectangle.fill === "#abcdef");
const ellipse = commandForDraft(extendDraft(beginDraft("ellipse", { x: 0, y: 0 }), { x: 20, y: 10 }), "#010203", 2, false);
expect("ellipse geometry", ellipse.kind === "ellipse" && ellipse.centerX === 10 && ellipse.centerY === 5 && ellipse.radiusX === 10 && ellipse.radiusY === 5);
const bounded = clampPoint(initial, { x: -12, y: 900 });
expect("bounds clamping", bounded.x === 0 && bounded.y === 200);

const added = addLayer(initial, initial.layers[0].id);
const duplicated = duplicateLayer(added.document, added.layerId);
expect("add and duplicate", duplicated.document.layers.length === 3 && duplicated.document.layers[2].id !== duplicated.document.layers[1].id);
let layered = renameLayer(duplicated.document, duplicated.layerId, "Ink");
layered = setLayerVisibility(layered, duplicated.layerId, false);
layered = setLayerOpacity(layered, duplicated.layerId, 0.35);
layered = moveLayer(layered, duplicated.layerId, (-1) as any);
expect("layer metadata and reorder", layered.layers[1].name === "Ink" && !layered.layers[1].isVisible && layered.layers[1].opacity === 0.35);
const deleted = deleteLayer(layered, duplicated.layerId);
expect("delete selects surviving layer", deleted.document.layers.length === 2 && deleted.layerId !== duplicated.layerId);

let history = createHistory(initial);
for (let index = 0; index < 55; index++) {
    const changed = appendCommand(history.document, initial.layers[0].id, { kind: "line", x1: index, y1: 0, x2: index, y2: 1, stroke: "#000000", strokeThickness: 1 });
    history = commitDocument(history, changed);
}
expect("fifty-step history bound", history.past.length === 50 && history.dirty);
const beforeUndo = history.document.layers[0].commands.length;
history = undo(history);
expect("undo", history.document.layers[0].commands.length === beforeUndo - 1 && history.future.length === 1);
history = redo(history);
expect("redo", history.document.layers[0].commands.length === beforeUndo);
history = markSaved(history);
expect("saved state", !history.dirty);
const savedHistory = commitDocument(history, appendCommand(history.document, initial.layers[0].id, { kind: "line", x1: 1, y1: 1, x2: 2, y2: 2, stroke: "#000000", strokeThickness: 1 }));
expect("edit after save is dirty", savedHistory.dirty);
expect("undo to saved identity is clean", !undo(savedHistory).dirty);

const portable = createImportedDocument(2, 3, "data:image/png;base64,iVBORw0KGgo=");
const roundTrip = parseProject(serializeProject(portable));
const image = roundTrip.layers[0].commands[0];
expect("portable embedded PNG round trip", image.kind === "image" && image.source.startsWith("data:image/png;base64,") && serializeProject(roundTrip) === serializeProject(portable));
const collisionProject = parseProject('{"format":"sharpaint","version":1,"width":1,"height":1,"layers":[{"id":"layer-100","name":"Imported","isVisible":true,"opacity":1,"commands":[]}]}');
expect("new layer ID does not collide after open", addLayer(collisionProject).document.layers[1].id !== "layer-100");

rejects("future version", '{"format":"sharpaint","version":2,"width":1,"height":1,"layers":[]}');
rejects("duplicate layer IDs", '{"format":"sharpaint","version":1,"width":1,"height":1,"layers":[{"id":"x","name":"a","isVisible":true,"opacity":1,"commands":[]},{"id":"x","name":"b","isVisible":true,"opacity":1,"commands":[]}]}');
rejects("non-finite geometry", '{"format":"sharpaint","version":1,"width":1e999,"height":1,"layers":[{"id":"x","name":"a","isVisible":true,"opacity":1,"commands":[]}]}');
rejects("unsupported command", '{"format":"sharpaint","version":1,"width":1,"height":1,"layers":[{"id":"x","name":"a","isVisible":true,"opacity":1,"commands":[{"kind":"filter"}]}]}');
rejects("unembedded image", '{"format":"sharpaint","version":1,"width":1,"height":1,"layers":[{"id":"x","name":"a","isVisible":true,"opacity":1,"commands":[{"kind":"image","source":"file.png","x":0,"y":0,"width":1,"height":1}]}]}');

console.log("SharpPaint model tests passed.");
