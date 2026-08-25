import { evaluateExpression } from "./expression";

export type GraphDrawingCommand =
    { kind: "line"; x1: number; y1: number; x2: number; y2: number; stroke: string; strokeThickness?: number } |
    { kind: "ellipse"; centerX: number; centerY: number; radiusX: number; radiusY: number; fill?: string; stroke?: string; strokeThickness?: number };

export interface GraphEquation { readonly id: number; readonly expression: string; readonly color: string; readonly visible: boolean; }
export interface GraphViewport { readonly centerX: number; readonly centerY: number; readonly scaleX: number; readonly scaleY: number; readonly width: number; readonly height: number; }
export interface GraphPoint { readonly x: number; readonly y: number; }

export const DEFAULT_VIEWPORT: GraphViewport = { centerX: 0, centerY: 0, scaleX: 32, scaleY: 32, width: 560, height: 360 };
export const GRAPH_COLORS: readonly string[] = ["#2563eb", "#dc2626", "#16a34a", "#9333ea", "#ea580c"];

function screenX(value: number, viewport: GraphViewport): number { return viewport.width / 2 + (value - viewport.centerX) * viewport.scaleX; }
function screenY(value: number, viewport: GraphViewport): number { return viewport.height / 2 - (value - viewport.centerY) * viewport.scaleY; }

export function sampleEquation(expression: string, viewport: GraphViewport, variables: Readonly<Record<string, number>> = {}): GraphPoint[] {
    let source = expression.trim();
    const equals = source.indexOf("=");
    if (equals >= 0) source = source.slice(equals + 1);
    const points: GraphPoint[] = [];
    for (let pixel = 0; pixel <= viewport.width; pixel += 2) {
        const x = viewport.centerX + (pixel - viewport.width / 2) / viewport.scaleX;
        try {
            const y = evaluateExpression(source, { angleUnit: "rad", variables: { ...variables, x } });
            if (Number.isFinite(y)) points.push({ x, y });
            else points.push({ x, y: Number.NaN });
        } catch (_error) { points.push({ x, y: Number.NaN }); }
    }
    return points;
}

export function graphCommands(equations: readonly GraphEquation[], viewport: GraphViewport, variables: Readonly<Record<string, number>> = {}): GraphDrawingCommand[] {
    const commands: GraphDrawingCommand[] = [];
    const axisX = screenY(0, viewport);
    const axisY = screenX(0, viewport);
    if (axisX >= 0 && axisX <= viewport.height) commands.push({ kind: "line", x1: 0, y1: axisX, x2: viewport.width, y2: axisX, stroke: "#64748b", strokeThickness: 1 });
    if (axisY >= 0 && axisY <= viewport.width) commands.push({ kind: "line", x1: axisY, y1: 0, x2: axisY, y2: viewport.height, stroke: "#64748b", strokeThickness: 1 });
    for (const equation of equations) {
        if (!equation.visible || equation.expression.trim() === "") continue;
        const points = sampleEquation(equation.expression, viewport, variables);
        let previous: GraphPoint | null = null;
        for (const point of points) {
            const x = screenX(point.x, viewport);
            const y = screenY(point.y, viewport);
            if (previous !== null) {
                const px = screenX(previous.x, viewport);
                const py = screenY(previous.y, viewport);
                if (Number.isFinite(y) && Number.isFinite(py) && Math.abs(y - py) < viewport.height)
                    commands.push({ kind: "line", x1: px, y1: py, x2: x, y2: y, stroke: equation.color, strokeThickness: 2 });
            }
            previous = Number.isFinite(point.y) ? point : null;
        }
    }
    return commands;
}

export function traceEquation(expression: string, x: number, variables: Readonly<Record<string, number>> = {}): GraphPoint {
    let source = expression;
    const equals = source.indexOf("=");
    if (equals >= 0) source = source.slice(equals + 1);
    return { x, y: evaluateExpression(source, { angleUnit: "rad", variables: { ...variables, x } }) };
}
