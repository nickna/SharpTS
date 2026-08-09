import { DesktopBridge, GuiVNode } from "dotnet:SharpTS.Gui";
export * from "./control-surface.generated";

export type Thickness = number | readonly [number, number] | readonly [number, number, number, number];
export type HorizontalAlignment = "left" | "center" | "right" | "stretch";
export type VerticalAlignment = "top" | "center" | "bottom" | "stretch";
export type Orientation = "horizontal" | "vertical";
export type ScrollBarVisibility = "auto" | "visible" | "hidden" | "disabled";
export type Theme = "system" | "light" | "dark";
export type Stretch = "none" | "fill" | "uniform" | "uniformToFill";
export type SelectionMode = "single" | "multiple";
export type Dock = "left" | "top" | "right" | "bottom";
export type FontWeight = "normal" | "medium" | "semibold" | "bold";
export type TextAlignment = "left" | "center" | "right" | "justify";
export interface RichTextRun {
    text: string; foreground?: string; fontSize?: number;
    fontWeight?: FontWeight; fontStyle?: "normal" | "italic";
}
export type DrawingCommand =
    { kind: "line"; x1: number; y1: number; x2: number; y2: number; stroke: string; strokeThickness?: number } |
    { kind: "rectangle"; x: number; y: number; width: number; height: number; fill?: string; stroke?: string; strokeThickness?: number } |
    { kind: "ellipse"; centerX: number; centerY: number; radiusX: number; radiusY: number; fill?: string; stroke?: string; strokeThickness?: number };
export interface SourceInfo { fileName: string; lineNumber: number; columnNumber: number; }
export interface GuiElement { readonly __guiElement: true; readonly type: any; readonly props: any; readonly key: string | null; readonly source: SourceInfo | null; }
export type GuiChild = GuiElement | string | number | boolean | null | undefined | readonly GuiChild[];
export interface TextualChildArray { readonly length: number; readonly [index: number]: TextualChild; }
export type TextualChild = string | number | boolean | null | undefined | TextualChildArray;
export type Component<P = {}> = (props: Readonly<P & { children?: GuiChild }>) => GuiChild;
export type SignalSetter<T> = (value: T | ((previous: T) => T)) => void;
export type StateSetter<T> = SignalSetter<T>;
export type Dispatch<A> = (action: A) => void;
/** Catches render/effect failures and native commit failures only after the previous native tree is restored. */
export interface ErrorBoundaryProps { readonly children?: GuiChild; readonly fallback: (error: unknown, reset: () => void) => GuiChild; }
export interface MutableRef<T> { current: T; }
export interface ControlRef<THandle> { readonly __controlHandle: THandle; readonly isAttached: boolean; focus(): boolean; }
export type WindowHandle = { readonly __windowHandle: never };
export type StackPanelHandle = { readonly __stackPanelHandle: never };
export type GridHandle = { readonly __gridHandle: never };
export type BorderHandle = { readonly __borderHandle: never };
export type TextBlockHandle = { readonly __textBlockHandle: never };
export type ButtonHandle = { readonly __buttonHandle: never };
export type TextBoxHandle = { readonly __textBoxHandle: never };
export interface KeyEvent { readonly key: string; readonly ctrl: boolean; readonly alt: boolean; readonly shift: boolean; readonly meta: boolean; readonly repeat: boolean; }
export interface CommonProps<THandle = unknown> {
    ref?: ControlRef<THandle>;
    width?: number; height?: number; minWidth?: number; minHeight?: number; maxWidth?: number; maxHeight?: number;
    margin?: Thickness; horizontalAlignment?: HorizontalAlignment; verticalAlignment?: VerticalAlignment;
    isVisible?: boolean; isEnabled?: boolean; opacity?: number; toolTip?: string; automationName?: string;
    classes?: readonly string[];
    gridRow?: number; gridColumn?: number; gridRowSpan?: number; gridColumnSpan?: number; dock?: Dock;
    canvasLeft?: number; canvasTop?: number;
    onKeyDown?: (event: KeyEvent) => boolean; onKeyUp?: (event: KeyEvent) => boolean;
}
export interface TextStyleProps { foreground?: string; fontFamily?: string; fontSize?: number; fontWeight?: FontWeight; fontStyle?: "normal" | "italic"; textAlignment?: TextAlignment; }
export interface ContentStyleProps extends TextStyleProps { background?: string; padding?: Thickness; cornerRadius?: number; horizontalContentAlignment?: HorizontalAlignment; verticalContentAlignment?: VerticalAlignment; }

export const Fragment: any = "Fragment";
export function ErrorBoundary(props: ErrorBoundaryProps): GuiElement { return props.children as any; }

function normalizeKey(key: any): string | null {
    return key === undefined || key === null ? null : String(key);
}
export function createElement(type: any, props: any, key?: any): GuiElement {
    return { __guiElement: true, type, props: props === undefined || props === null ? {} : props, key: normalizeKey(key), source: null };
}
export function createDevElement(type: any, props: any, key: any, source: any): GuiElement {
    const element = createElement(type, props, key);
    const info = source === undefined || source === null ? null : {
        fileName: source.fileName === undefined ? "" : source.fileName,
        lineNumber: source.lineNumber === undefined ? 0 : source.lineNumber,
        columnNumber: source.columnNumber === undefined ? 0 : source.columnNumber,
    };
    return { ...element, source: info };
}

interface SignalState { value: any; subscribers: ReactiveRoot[]; }
let activeSignalCollector: SignalState[] | null = null;
function contains(items: any[], item: any): boolean { return items.indexOf(item) >= 0; }
function remove(items: any[], item: any): void { const index = items.indexOf(item); if (index >= 0) items.splice(index, 1); }

interface CreateSignal { <T>(initial: T): [() => T, SignalSetter<T>]; }
function createSignalImpl(initial: any): [() => any, SignalSetter<any>] {
    const state: SignalState = { value: initial, subscribers: [] };
    const get = (): any => {
        if (activeSignalCollector !== null && !contains(activeSignalCollector, state)) activeSignalCollector.push(state);
        return state.value;
    };
    const set = (next: any): void => {
        const value = typeof next === "function" ? (next as any)(state.value) : next;
        if (Object.is(state.value, value)) return;
        state.value = value;
        for (const subscriber of state.subscribers.slice()) subscriber.invalidate();
    };
    return [get, set];
}
export const createSignal: CreateSignal = createSignalImpl;

interface StateHook { kind: "state"; value: any; pending: any[]; setter: (value: any) => void; }
interface ReducerHook { kind: "reducer"; value: any; pending: any[]; reducer: (state: any, action: any) => any; dispatch: (action: any) => void; }
interface MemoHook { kind: "memo"; value: any; deps: readonly unknown[] | undefined; }
interface RefHook { kind: "ref"; ref: MutableRef<any>; }
interface ControlRefHook { kind: "controlRef"; ref: any; }
interface EffectHook { kind: "effect"; effect: () => any; deps: readonly unknown[] | undefined; cleanup: (() => void) | null; changed: boolean; }
type Hook = StateHook | ReducerHook | MemoHook | RefHook | ControlRefHook | EffectHook;
interface ErrorBoundaryState { path: string; error: any; root: ReactiveRoot; seen: boolean; }
interface ComponentState { path: string; type: any; hooks: Hook[]; nextHooks: Hook[]; hookIndex: number; root: ReactiveRoot; seen: boolean; mounted: boolean; boundary: ErrorBoundaryState | null; }
interface LogicalFiber {
    readonly kind: "component" | "fragment" | "intrinsic" | "text";
    readonly key: string | null;
    readonly path: string;
    readonly type: any;
    readonly children: readonly LogicalFiber[];
}
interface MaterializedChildren {
    readonly fibers: LogicalFiber[];
    readonly nodes: GuiVNode[];
}
let activeComponent: ComponentState | null = null;

function requireComponent(name: string): ComponentState {
    if (activeComponent === null) throw new Error(name + " may only be called while rendering a function component.");
    return activeComponent;
}
function depsEqual(left: readonly unknown[] | undefined, right: readonly unknown[] | undefined): boolean {
    if (left === undefined || right === undefined || left.length !== right.length) return false;
    for (let i = 0; i < left.length; i++) if (!Object.is(left[i], right[i])) return false;
    return true;
}
function oldHook(component: ComponentState, kind: Hook["kind"]): any {
    const value = component.hooks[component.hookIndex];
    if (value === undefined) return null;
    if (value.kind !== kind) throw new Error("Hook order changed in component at " + component.path + ".");
    return value;
}
function appendHook(component: ComponentState, hook: Hook): void { component.nextHooks.push(hook); component.hookIndex++; }

interface UseState { <T>(initial: T | (() => T)): [T, StateSetter<T>]; }
function useStateImpl(initial: any): any[] {
    const component = requireComponent("useState");
    const old: StateHook | null = oldHook(component, "state");
    let hook: StateHook;
    if (old === null) {
        hook = { kind: "state", value: typeof initial === "function" ? (initial as any)() : initial, pending: [], setter: null as any };
        hook.setter = (next: any): void => {
            if (!component.mounted) return;
            hook.pending.push(next);
            component.root.invalidate();
        };
    } else hook = old;
    let value = hook.value;
    for (const update of hook.pending) value = typeof update === "function" ? update(value) : update;
    const nextHook: StateHook = { ...hook, value, pending: hook.pending, setter: hook.setter };
    appendHook(component, nextHook);
    return [value, hook.setter];
}
export const useState: UseState = useStateImpl as any;

interface UseReducer { <S, A>(reducer: (state: S, action: A) => S, initial: S): [S, Dispatch<A>]; }
function useReducerImpl(reducer: any, initial: any): any[] {
    const component = requireComponent("useReducer");
    const old: ReducerHook | null = oldHook(component, "reducer");
    let hook: ReducerHook;
    if (old === null) {
        hook = { kind: "reducer", value: initial, pending: [], reducer: reducer as any, dispatch: null as any };
        hook.dispatch = (action: any): void => {
            if (!component.mounted) return;
            hook.pending.push(action);
            component.root.invalidate();
        };
    } else { hook = old; hook.reducer = reducer as any; }
    let value = hook.value;
    for (const action of hook.pending) value = reducer(value, action);
    const nextHook: ReducerHook = { ...hook, value, pending: hook.pending, reducer: reducer as any, dispatch: hook.dispatch };
    appendHook(component, nextHook);
    return [value, hook.dispatch];
}
export const useReducer: UseReducer = useReducerImpl as any;

interface UseMemo { <T>(factory: () => T, deps: readonly unknown[]): T; }
function useMemoImpl(factory: any, deps: readonly unknown[]): any {
    const component = requireComponent("useMemo");
    const old: MemoHook | null = oldHook(component, "memo");
    const hook: MemoHook = old !== null && depsEqual(old.deps, deps) ? old : { kind: "memo", value: factory(), deps };
    appendHook(component, hook);
    return hook.value;
}
export const useMemo: UseMemo = useMemoImpl;
interface UseCallback { <T extends (...args: any[]) => any>(callback: T, deps: readonly unknown[]): T; }
function useCallbackImpl(callback: any, deps: readonly unknown[]): any { return useMemoImpl(() => callback, deps); }
export const useCallback: UseCallback = useCallbackImpl;
interface UseRef { <T>(initial: T): MutableRef<T>; }
function useRefImpl(initial: any): MutableRef<any> {
    const component = requireComponent("useRef");
    const old: RefHook | null = oldHook(component, "ref");
    const hook: RefHook = old === null ? { kind: "ref", ref: { current: initial } } : old;
    appendHook(component, hook);
    return hook.ref;
}
export const useRef: UseRef = useRefImpl;
export function useEffect(effect: () => any, deps?: readonly unknown[]): void {
    const component = requireComponent("useEffect");
    const old: EffectHook | null = oldHook(component, "effect");
    const changed = old === null || deps === undefined || !depsEqual(old.deps, deps);
    appendHook(component, { kind: "effect", effect, deps, cleanup: old === null ? null : old.cleanup, changed });
}

interface CreateControlRef { <THandle>(): ControlRef<THandle>; }
function createControlRefImpl(): any { return DesktopBridge.CreateRef(); }
export const createControlRef: CreateControlRef = createControlRefImpl;
interface UseControlRef { <THandle>(): ControlRef<THandle>; }
function useControlRefImpl(): any {
    const component = requireComponent("useControlRef");
    const old: ControlRefHook | null = oldHook(component, "controlRef");
    const hook: ControlRefHook = old === null ? { kind: "controlRef", ref: createControlRefImpl() } : old;
    appendHook(component, hook);
    return hook.ref;
}
export const useControlRef: UseControlRef = useControlRefImpl;

function flatten(children: GuiChild, output: GuiChild[]): void {
    if (children === null || children === undefined || typeof children === "boolean") return;
    if (Array.isArray(children)) { for (const child of children) flatten(child, output); return; }
    output.push(children);
}
function textContent(children: GuiChild): string {
    const values: GuiChild[] = []; flatten(children, values);
    let result = "";
    for (const value of values) {
        if (typeof value !== "string" && typeof value !== "number") throw new Error("This control accepts textual children only.");
        result += String(value);
    }
    return result;
}
function thickness(value: Thickness | undefined): number[] {
    if (value === undefined) return [0, 0, 0, 0];
    if (typeof value === "number") return [value, value, value, value];
    const items: any = value;
    if (items.length === 2) return [items[1], items[0], items[1], items[0]];
    return [items[3], items[0], items[1], items[2]];
}
function action(handler: any): any { let result: any = null; if (typeof handler === "function") result = (): void => handler(); return result; }
function stringAction(handler: any): any { let result: any = null; if (typeof handler === "function") result = (value: string): void => handler(value); return result; }
function boolAction(handler: any): any { let result: any = null; if (typeof handler === "function") result = (value: boolean): void => handler(value); return result; }
function numberAction(handler: any): any { let result: any = null; if (typeof handler === "function") result = (value: number): void => handler(value); return result; }
function indicesAction(handler: any): any { let result: any = null; if (typeof handler === "function") result = (value: number[]): void => handler(value); return result; }
function nullableNumberAction(handler: any): any { let result: any = null; if (typeof handler === "function") result = (value: any): void => handler(value); return result; }
function nullableStringAction(handler: any): any { let result: any = null; if (typeof handler === "function") result = (value: any): void => handler(value); return result; }
function keyAction(handler: any): any {
    let result: any = null;
    if (typeof handler === "function") {
        result = (key: string, ctrl: boolean, alt: boolean, shift: boolean, meta: boolean, repeat: boolean): boolean =>
            handler({ key, ctrl, alt, shift, meta, repeat }) === true;
    }
    return result;
}

function hasProperty(value: any, name: string): boolean {
    const keys = Object.keys(value);
    for (const key of keys) if (key === name) return true;
    return false;
}

function withCommon(node: GuiVNode, safe: any): GuiVNode {
    const margin = thickness(safe.margin);
    return DesktopBridge.WithCommon(node,
        safe.width === undefined ? NaN : safe.width, safe.height === undefined ? NaN : safe.height,
        safe.minWidth === undefined ? 0 : safe.minWidth, safe.minHeight === undefined ? 0 : safe.minHeight,
        safe.maxWidth === undefined ? Infinity : safe.maxWidth, safe.maxHeight === undefined ? Infinity : safe.maxHeight,
        margin[0], margin[1], margin[2], margin[3],
        safe.horizontalAlignment === undefined ? "stretch" : safe.horizontalAlignment,
        safe.verticalAlignment === undefined ? "stretch" : safe.verticalAlignment,
        safe.isVisible === undefined ? true : safe.isVisible, safe.isEnabled === undefined ? true : safe.isEnabled,
        safe.opacity === undefined ? 1 : safe.opacity, safe.toolTip === undefined ? null : safe.toolTip,
        safe.automationName === undefined ? null : safe.automationName,
        safe.classes === undefined ? [] : safe.classes.slice(),
        safe.gridRow === undefined ? 0 : safe.gridRow, safe.gridColumn === undefined ? 0 : safe.gridColumn,
        safe.gridRowSpan === undefined ? 1 : safe.gridRowSpan, safe.gridColumnSpan === undefined ? 1 : safe.gridColumnSpan,
        safe.dock === undefined ? "left" : safe.dock,
        safe.canvasLeft === undefined ? NaN : safe.canvasLeft,
        safe.canvasTop === undefined ? NaN : safe.canvasTop,
        keyAction(safe.onKeyDown), keyAction(safe.onKeyUp),
        hasProperty(safe, "onKeyDown"), hasProperty(safe, "onKeyUp")) as any;
}

function withStyle(node: GuiVNode, safe: any): GuiVNode {
    const padding = thickness(safe.padding);
    return DesktopBridge.WithStyle(node,
        safe.background === undefined ? null : safe.background,
        safe.foreground === undefined ? null : safe.foreground,
        padding[0], padding[1], padding[2], padding[3],
        safe.cornerRadius === undefined ? 0 : safe.cornerRadius,
        safe.fontSize === undefined ? NaN : safe.fontSize,
        safe.fontWeight === undefined ? "normal" : safe.fontWeight,
        safe.fontStyle === undefined ? "normal" : safe.fontStyle,
        safe.fontFamily === undefined ? null : safe.fontFamily,
        safe.textAlignment === undefined ? "left" : safe.textAlignment) as any;
}

function source(node: GuiVNode, info: SourceInfo | null): GuiVNode {
    return info === null ? node : DesktopBridge.WithSource(node, info.fileName, info.lineNumber, info.columnNumber) as any;
}

let nextFunctionId = 1;
const functionTypes: any[] = [];
const functionIds: number[] = [];
function functionId(type: any): number {
    const index = functionTypes.indexOf(type);
    if (index >= 0) return functionIds[index];
    const id = nextFunctionId++; functionTypes.push(type); functionIds.push(id); return id;
}

class ReactiveRoot {
    private managed: any = null;
    private scheduled = false;
    private disposed = false;
    private rendering = false;
    private dependencies: SignalState[] = [];
    private components: ComponentState[] = [];
    private boundaries: ErrorBoundaryState[] = [];
    private fibers: LogicalFiber[] = [];
    public constructor(
        private readonly element: GuiChild,
        private readonly onUnhandledError: ((error: unknown) => void) | null = null) {}
    public setManaged(root: any): void { this.managed = root; }
    public invalidate(): void {
        if (this.disposed || this.scheduled) return;
        if (this.rendering) throw new Error("State cannot be updated while rendering.");
        this.scheduled = true;
        DesktopBridge.QueueMicrotask((): void => {
            this.scheduled = false;
            if (this.disposed) return;
            try { this.renderNow(); }
            catch (error) { this.failWindow(error); }
        });
    }
    private component(path: string, type: any, boundary: ErrorBoundaryState | null): ComponentState {
        for (const existing of this.components) if (existing.path === path && existing.type === type) {
            existing.seen = true; existing.boundary = boundary; return existing;
        }
        const created: ComponentState = { path, type, hooks: [], nextHooks: [], hookIndex: 0, root: this, seen: true, mounted: true, boundary };
        this.components.push(created); return created;
    }
    private boundary(path: string): ErrorBoundaryState {
        for (const existing of this.boundaries) if (existing.path === path) { existing.seen = true; return existing; }
        const created: ErrorBoundaryState = { path, error: null, root: this, seen: true };
        this.boundaries.push(created); return created;
    }
    private materialize(child: GuiChild, path: string, transparentPrefix: string | null = null,
        nearestBoundary: ErrorBoundaryState | null = null): MaterializedChildren {
        const flat: GuiChild[] = []; flatten(child, flat);
        const explicitKeys: string[] = [];
        for (const item of flat) {
            if (typeof item !== "string" && typeof item !== "number") {
                const element = item as GuiElement;
                if (element.__guiElement !== true) throw new Error("Unsupported @sharpts/gui child value.");
                if (element.key !== null) {
                    if (contains(explicitKeys, element.key)) throw new Error("Duplicate sibling key '" + element.key + "'.");
                    explicitKeys.push(element.key);
                }
            }
        }
        const fibers: LogicalFiber[] = [];
        const nodes: GuiVNode[] = [];
        for (let index = 0; index < flat.length; index++) {
            const item = flat[index];
            const positionalSegment = String(index);
            if (typeof item === "string" || typeof item === "number") {
                const textPath = path + "/" + positionalSegment;
                nodes.push(DesktopBridge.CreateTextBlock(String(item), NaN, "normal", "normal", "noWrap", "left", null,
                    transparentPrefix === null ? null : transparentPrefix + "/" + positionalSegment, null) as any);
                fibers.push({ kind: "text", key: null, path: textPath, type: "TextBlock", children: [] });
                continue;
            }
            const element = item as GuiElement;
            const segment = element.key === null ? String(index) : "$" + element.key;
            const childPath = path + "/" + segment;
            if (element.type === ErrorBoundary) {
                const state = this.boundary(childPath + ":boundary");
                const safe: ErrorBoundaryProps = element.props || {} as any;
                if (typeof safe.fallback !== "function") throw new Error("ErrorBoundary requires a fallback function.");
                const reset = (): void => { if (state.error !== null) { state.error = null; state.root.invalidate(); } };
                const prefix = element.key !== null || flat.length > 1
                    ? (transparentPrefix === null ? segment : transparentPrefix + "/" + segment)
                    : transparentPrefix;
                let nested: MaterializedChildren;
                if (state.error !== null) {
                    nested = this.materialize(safe.fallback(state.error, reset), state.path, prefix, nearestBoundary);
                } else {
                    try {
                        nested = this.materialize(safe.children, state.path, prefix, state);
                    } catch (error) {
                        state.error = error;
                        for (const component of this.components)
                            if (component.path.indexOf(state.path + "/") === 0) component.seen = false;
                        nested = this.materialize(safe.fallback(error, reset), state.path, prefix, nearestBoundary);
                    }
                }
                fibers.push({ kind: "component", key: element.key, path: state.path, type: ErrorBoundary, children: nested.fibers });
                for (const nestedNode of nested.nodes) nodes.push(nestedNode);
                continue;
            }
            if (typeof element.type === "function") {
                const id = functionId(element.type);
                const state = this.component(childPath + ":c" + id, element.type, nearestBoundary);
                state.hookIndex = 0; state.nextHooks = [];
                const previous = activeComponent; activeComponent = state;
                let rendered: GuiChild;
                try { rendered = element.type(element.props); }
                finally { activeComponent = previous; }
                if (state.hookIndex !== state.hooks.length && state.hooks.length !== 0)
                    throw new Error("Hook count changed in component at " + state.path + ".");
                const prefix = element.key !== null || flat.length > 1
                    ? (transparentPrefix === null ? segment : transparentPrefix + "/" + segment)
                    : transparentPrefix;
                const nested = this.materialize(rendered, state.path, prefix, nearestBoundary);
                fibers.push({ kind: "component", key: element.key, path: state.path, type: element.type, children: nested.fibers });
                for (const nestedNode of nested.nodes) nodes.push(nestedNode);
                continue;
            }
            if (element.type === Fragment) {
                const prefix = element.key !== null || flat.length > 1
                    ? (transparentPrefix === null ? segment : transparentPrefix + "/" + segment)
                    : transparentPrefix;
                const nested = this.materialize((element.props || {}).children, childPath, prefix, nearestBoundary);
                fibers.push({ kind: "fragment", key: element.key, path: childPath, type: Fragment, children: nested.fibers });
                for (const nestedNode of nested.nodes) nodes.push(nestedNode);
                continue;
            }
            const nativeKey = transparentPrefix === null
                ? element.key
                : transparentPrefix + "/" + segment;
            const intrinsic = this.intrinsic(element, childPath, nativeKey, nearestBoundary);
            fibers.push({ kind: "intrinsic", key: element.key, path: childPath, type: element.type, children: intrinsic.fibers });
            nodes.push(intrinsic.node);
        }
        return { fibers, nodes };
    }
    private intrinsic(element: GuiElement, path: string, nativeKey: string | null,
        nearestBoundary: ErrorBoundaryState | null): { node: GuiVNode; fibers: LogicalFiber[] } {
        const safe: any = element.props || {};
        const ref: any = safe.ref === undefined ? null : safe.ref;
        const textual = element.type === "TextBlock" || element.type === "Button" ||
            element.type === "CheckBox" || element.type === "RadioButton" ||
            element.type === "ToggleSwitch";
        const children = textual ? { fibers: [], nodes: [] } : this.materialize(safe.children, path, null, nearestBoundary);
        const key: any = nativeKey;
        let node: GuiVNode;
        const pad = thickness(safe.padding); const border = thickness(safe.borderThickness);
        switch (element.type) {
            case "Window": node = DesktopBridge.CreateWindow(safe.title === undefined ? "SharpTS GUI" : safe.title, safe.width === undefined ? 720 : safe.width, safe.height === undefined ? 480 : safe.height, safe.canResize === undefined ? true : safe.canResize, safe.theme === undefined ? "system" : safe.theme, children.nodes, key, ref); break;
            case "StackPanel": case "ToolBar": node = DesktopBridge.CreateStackPanel(element.type, safe.spacing === undefined ? 0 : safe.spacing, element.type === "ToolBar" ? "horizontal" : (safe.orientation === undefined ? "vertical" : safe.orientation), children.nodes, key, ref); break;
            case "WrapPanel": node = DesktopBridge.CreateWrapPanel(safe.spacing === undefined ? 0 : safe.spacing, safe.orientation === undefined ? "horizontal" : safe.orientation, children.nodes, key, ref); break;
            case "DockPanel": node = DesktopBridge.CreateDockPanel(safe.lastChildFill === undefined ? true : safe.lastChildFill, children.nodes, key, ref); break;
            case "Grid": node = DesktopBridge.CreateGrid(safe.rows || "", safe.columns || "", children.nodes, key, ref); break;
            case "Border": case "StatusBar": node = DesktopBridge.CreateBorder(element.type, pad[0], pad[1], pad[2], pad[3], safe.background || null, safe.borderBrush || null, border[0], border[1], border[2], border[3], safe.cornerRadius || 0, children.nodes, key, ref); break;
            case "ScrollViewer": node = DesktopBridge.CreateScrollViewer(safe.horizontalScrollBarVisibility || "auto", safe.verticalScrollBarVisibility || "auto", children.nodes, key, ref); break;
            case "Separator": node = DesktopBridge.CreateSeparator(key, ref); break;
            case "TextBlock": node = DesktopBridge.CreateTextBlock(textContent(safe.children), safe.fontSize === undefined ? NaN : safe.fontSize, safe.fontWeight || "normal", safe.fontStyle || "normal", safe.textWrapping || "noWrap", safe.textAlignment || "left", safe.foreground || null, key, ref); break;
            case "Button": case "CheckBox": case "RadioButton": case "ToggleSwitch": case "MenuItem": node = DesktopBridge.CreateContentControl(element.type, element.type === "MenuItem" ? (safe.header || "") : textContent(safe.children), safe.isChecked === true, safe.groupName || null, action(safe.onClick), boolAction(safe.onCheckedChanged), safe.background || null, safe.foreground || null, pad[0], pad[1], pad[2], pad[3], safe.fontSize === undefined ? NaN : safe.fontSize, safe.fontWeight || "normal", safe.horizontalContentAlignment || "center", safe.verticalContentAlignment || "center", children.nodes, key, ref); break;
            case "TextBox": case "PasswordBox": node = DesktopBridge.CreateTextBox(element.type, element.type === "PasswordBox" ? (safe.value || "") : (safe.text || ""), safe.placeholder || null, safe.isReadOnly === true, safe.acceptsReturn === true, safe.maxLength === undefined ? 0 : safe.maxLength, element.type === "PasswordBox" && safe.revealPassword !== true, stringAction(element.type === "PasswordBox" ? safe.onValueChanged : safe.onTextChanged), key, ref); break;
            case "ComboBox": node = DesktopBridge.CreateComboBox((safe.items || []).slice(), safe.selectedIndex === undefined ? -1 : safe.selectedIndex, numberAction(safe.onSelectionChanged), key, ref); break;
            case "ListBox": node = DesktopBridge.CreateListBox((safe.items || []).slice(), (safe.selectedIndices || []).slice(), safe.selectionMode || "single", indicesAction(safe.onSelectionChanged), key, ref); break;
            case "ItemsControl": case "TreeView": case "Canvas": node = DesktopBridge.CreateItemsControl(element.type, children.nodes, key, ref); break;
            case "VirtualizingList": node = DesktopBridge.CreateVirtualizingList((safe.selectedIndices || []).slice(), safe.selectionMode || "single", indicesAction(safe.onSelectionChanged), children.nodes, key, ref); break;
            case "TreeViewItem": node = DesktopBridge.CreateTreeViewItem(safe.header, safe.isExpanded === true, boolAction(safe.onExpandedChanged), children.nodes, key, ref); break;
            case "RichTextBlock": node = DesktopBridge.CreateRichTextBlock(JSON.stringify(safe.runs || []), key, ref); break;
            case "DrawingCanvas": node = DesktopBridge.CreateDrawingCanvas(JSON.stringify(safe.commands || []), key, ref); break;
            case "NumericUpDown": node = DesktopBridge.CreateNumericUpDown(safe.minimum === undefined ? 0 : safe.minimum, safe.maximum === undefined ? 100 : safe.maximum, safe.increment === undefined ? 1 : safe.increment, safe.value === undefined ? null : safe.value, nullableNumberAction(safe.onValueChanged), key, ref); break;
            case "DatePicker": case "TimePicker": node = DesktopBridge.CreateDateTimePicker(element.type, safe.value === undefined ? null : safe.value, nullableStringAction(safe.onValueChanged), key, ref); break;
            case "Slider": node = DesktopBridge.CreateSlider(safe.minimum === undefined ? 0 : safe.minimum, safe.maximum === undefined ? 100 : safe.maximum, safe.value === undefined ? 0 : safe.value, numberAction(safe.onValueChanged), key, ref); break;
            case "ProgressBar": node = DesktopBridge.CreateProgressBar(safe.minimum === undefined ? 0 : safe.minimum, safe.maximum === undefined ? 100 : safe.maximum, safe.value === undefined ? 0 : safe.value, key, ref); break;
            case "Image": node = DesktopBridge.CreateImage(safe.source, safe.stretch || "uniform", action(safe.onLoad), stringAction(safe.onError), key, ref); break;
            case "TabControl": node = DesktopBridge.CreateTabControl(safe.selectedIndex === undefined ? 0 : safe.selectedIndex, numberAction(safe.onSelectionChanged), children.nodes, key, ref); break;
            case "TabItem": node = DesktopBridge.CreateTabItem(safe.header, children.nodes, key, ref); break;
            case "Menu": node = DesktopBridge.CreateMenu(children.nodes, key, ref); break;
            default: throw new Error("Unknown @sharpts/gui TSX tag: " + element.type);
        }
        node = DesktopBridge.WithSpecifiedProperties(
            source(withCommon(withStyle(node, safe), safe), element.source),
            Object.keys(safe)) as any;
        if (nearestBoundary !== null) node = DesktopBridge.WithBoundary(node, nearestBoundary.path) as any;
        return { node, fibers: children.fibers };
    }
    public renderNow(): void {
        if (this.disposed) return;
        this.rendering = true;
        for (const component of this.components) component.seen = false;
        for (const boundary of this.boundaries) boundary.seen = false;
        const nextDependencies: SignalState[] = [];
        const previousCollector = activeSignalCollector; activeSignalCollector = nextDependencies;
        let recoveredCommitError: any = null;
        try {
            const materialized = this.materialize(this.element, "root");
            if (materialized.nodes.length !== 1) throw new Error("A desktop window requires exactly one Window root.");
            this.managed.Render(materialized.nodes[0]);
            this.fibers = materialized.fibers;
        } catch (error) {
            const value: any = error as any;
            let boundaryPath = value === null || value === undefined
                ? null
                : (value.BoundaryPath === undefined ? value.boundaryPath : value.BoundaryPath);
            if (typeof boundaryPath !== "string") {
                const message = value === null || value === undefined
                    ? ""
                    : String(value.message === undefined ? (value.Message === undefined ? value : value.Message) : value.message);
                const prefix = "[SharpTSRecoverableCommit:";
                const start = message.indexOf(prefix);
                const end = start < 0 ? -1 : message.indexOf("]", start + prefix.length);
                if (start >= 0 && end > start) boundaryPath = message.substring(start + prefix.length, end);
            }
            if (typeof boundaryPath !== "string") throw error;
            let target: ErrorBoundaryState | null = null;
            for (const boundary of this.boundaries) if (boundary.path === boundaryPath) { target = boundary; break; }
            if (target === null) throw error;
            target.error = error;
            target.seen = true;
            for (const component of this.components) {
                component.nextHooks = [];
                if (component.path.indexOf(target.path + "/") === 0) component.seen = false;
            }
            recoveredCommitError = error;
        } finally { activeSignalCollector = previousCollector; this.rendering = false; }
        if (recoveredCommitError !== null) { this.renderNow(); return; }
        this.commitComponents();
        this.boundaries = this.boundaries.filter(boundary => boundary.seen);
        for (const dependency of this.dependencies) if (!contains(nextDependencies, dependency)) remove(dependency.subscribers, this);
        for (const dependency of nextDependencies) if (!contains(dependency.subscribers, this)) dependency.subscribers.push(this);
        this.dependencies = nextDependencies;
    }
    private commitComponents(): void {
        const removed = this.components.filter(component => !component.seen).sort((a, b) => b.path.length - a.path.length);
        for (const component of removed) { component.mounted = false; this.cleanup(component); }
        this.components = this.components.filter(component => component.seen);
        const childFirst = this.components.slice().sort((a, b) => b.path.length - a.path.length);
        for (const component of childFirst) {
            for (const hook of component.nextHooks) {
                if (hook.kind === "state" || hook.kind === "reducer") hook.pending.splice(0, hook.pending.length);
                if (hook.kind === "effect" && hook.changed && hook.cleanup !== null) {
                    const cleanup = hook.cleanup;
                    try { cleanup(); }
                    catch (error) { this.captureEffectError(component, error); }
                }
            }
            component.hooks = component.nextHooks;
        }
        for (const component of childFirst) for (const hook of component.hooks) if (hook.kind === "effect" && hook.changed) {
            try {
                const cleanup = hook.effect(); hook.cleanup = typeof cleanup === "function" ? cleanup : null;
            } catch (error) { this.captureEffectError(component, error); }
            hook.changed = false;
        }
    }
    private captureEffectError(component: ComponentState, error: any): void {
        const boundary = component.boundary;
        if (this.disposed || boundary === null) throw error;
        boundary.error = error;
        this.invalidate();
    }
    private cleanup(component: ComponentState): void {
        for (let i = component.hooks.length - 1; i >= 0; i--) {
            const hook = component.hooks[i];
            if (hook.kind === "effect" && hook.cleanup !== null) {
                const cleanup = hook.cleanup;
                try { cleanup(); }
                catch (error) { this.captureEffectError(component, error); }
                hook.cleanup = null;
            }
        }
    }
    private failWindow(error: unknown): void {
        const report = this.onUnhandledError;
        if (this.managed !== null) this.managed.Dispose();
        if (report === null) throw error;
        report(error);
    }
    public disposeFromManaged(): void {
        if (this.disposed) return; this.disposed = true; this.scheduled = false;
        for (const component of this.components.slice().sort((a, b) => b.path.length - a.path.length)) {
            component.mounted = false;
            this.cleanup(component);
        }
        for (const dependency of this.dependencies) remove(dependency.subscribers, this);
        this.dependencies = []; this.components = []; this.boundaries = []; this.fibers = []; this.managed = null;
    }
}

export interface DesktopRoot { readonly isDisposed: boolean; dispose(): void; }
export function renderDesktop(element: GuiChild): DesktopRoot {
    const runner = new ReactiveRoot(element);
    const managed: any = DesktopBridge.CreateDesktopRoot((): void => runner.disposeFromManaged());
    runner.setManaged(managed);
    try { runner.renderNow(); }
    catch (error) { managed.Dispose(); throw error; }
    return {
        get isDisposed(): boolean { return managed.IsDisposed; },
        dispose(): void { managed.Dispose(); },
    };
}

export type ItemKey = string | number;
export type ItemTemplate<T> = (item: T, index: number) => GuiChild;
export interface VirtualListProps<T> extends CommonProps<unknown> {
    key?: ItemKey;
    items: readonly T[];
    itemKey: (item: T, index: number) => ItemKey;
    renderItem: ItemTemplate<T>;
    startIndex: number;
    visibleCount: number;
    overscan?: number;
    selectedIndices?: readonly number[];
    selectionMode?: SelectionMode;
    onSelectionChanged?: (indices: number[]) => void;
}
interface CreateVirtualList { <T>(props: VirtualListProps<T>): GuiElement; }
function createVirtualListImpl(props: VirtualListProps<any>): GuiElement {
    const overscan = props.overscan === undefined ? 2 : Math.max(0, Math.floor(props.overscan));
    const start = Math.max(0, Math.floor(props.startIndex) - overscan);
    const end = Math.min(props.items.length, Math.floor(props.startIndex) + Math.max(0, Math.floor(props.visibleCount)) + overscan);
    const children: GuiElement[] = [];
    for (let index = start; index < end; index++) {
        const item = props.items[index];
        children.push(createElement(Fragment, { children: props.renderItem(item, index) }, props.itemKey(item, index)));
    }
    const selected = (props.selectedIndices || []).filter(index => index >= start && index < end).map(index => index - start);
    const changed = props.onSelectionChanged === undefined ? undefined
        : (indices: number[]): void => props.onSelectionChanged!(indices.map(index => index + start));
    return createElement("VirtualizingList", { ...props, selectedIndices: selected, onSelectionChanged: changed, children }, props.key);
}
export const createVirtualList: CreateVirtualList = createVirtualListImpl;

export interface TreeProps<T> extends CommonProps<unknown> {
    key?: ItemKey;
    items: readonly T[];
    itemKey: (item: T) => ItemKey;
    itemLabel: (item: T) => string;
    childrenOf: (item: T) => readonly T[];
    isExpanded?: (item: T) => boolean;
    onExpandedChanged?: (item: T, expanded: boolean) => void;
}
interface CreateTree { <T>(props: TreeProps<T>): GuiElement; }
function createTreeImpl(props: TreeProps<any>): GuiElement {
    const renderNodes = (items: readonly any[]): GuiElement[] => items.map(item =>
        createElement("TreeViewItem", {
            header: props.itemLabel(item),
            isExpanded: props.isExpanded === undefined ? false : props.isExpanded(item),
            onExpandedChanged: props.onExpandedChanged === undefined ? undefined
                : (expanded: boolean): void => props.onExpandedChanged!(item, expanded),
            children: renderNodes(props.childrenOf(item)),
        }, props.itemKey(item)));
    return createElement("TreeView", { ...props, children: renderNodes(props.items) }, props.key);
}
export const createTree: CreateTree = createTreeImpl;

export interface DataGridColumn<T> {
    key: string;
    header: string;
    renderCell: (item: T, rowIndex: number) => GuiChild;
}
export interface VirtualDataGridProps<T> extends CommonProps<unknown> {
    key?: ItemKey;
    items: readonly T[];
    columns: readonly DataGridColumn<T>[];
    rowKey: (item: T, rowIndex: number) => ItemKey;
    startIndex: number;
    visibleCount: number;
    overscan?: number;
}
interface CreateVirtualDataGrid { <T>(props: VirtualDataGridProps<T>): GuiElement; }
function createVirtualDataGridImpl(props: VirtualDataGridProps<any>): GuiElement {
    const overscan = props.overscan === undefined ? 1 : Math.max(0, Math.floor(props.overscan));
    const start = Math.max(0, Math.floor(props.startIndex) - overscan);
    const end = Math.min(props.items.length, Math.floor(props.startIndex) + Math.max(0, Math.floor(props.visibleCount)) + overscan);
    const children: GuiElement[] = [];
    for (let column = 0; column < props.columns.length; column++) {
        const definition = props.columns[column];
        children.push(createElement("Border", { gridRow: 0, gridColumn: column, children:
            createElement("TextBlock", { children: definition.header }) }, "header:" + definition.key));
    }
    for (let index = start; index < end; index++) {
        const item = props.items[index];
        const rowKey = String(props.rowKey(item, index));
        for (let column = 0; column < props.columns.length; column++) {
            const definition = props.columns[column];
            children.push(createElement("Border", {
                gridRow: index - start + 1,
                gridColumn: column,
                children: props.columns[column].renderCell(item, index),
            }, rowKey + ":" + definition.key));
        }
    }
    const rows: string[] = ["auto"];
    for (let index = start; index < end; index++) rows.push("auto");
    const columns: string[] = [];
    for (let index = 0; index < props.columns.length; index++) columns.push("*");
    return createElement("Grid", { ...props, rows: rows.join(","), columns: columns.join(","), children }, props.key);
}
export const createVirtualDataGrid: CreateVirtualDataGrid = createVirtualDataGridImpl;

export type DesktopShutdownMode = "onLastWindowClose" | "onMainWindowClose" | "explicit";
export type DesktopControlKind =
    "Control" | "Window" | "StackPanel" | "ToolBar" | "WrapPanel" | "DockPanel" | "Grid" |
    "Border" | "StatusBar" | "ScrollViewer" | "TextBlock" | "Button" | "TextBox" |
    "PasswordBox" | "CheckBox" | "RadioButton" | "ToggleSwitch" | "ComboBox" | "ListBox" |
    "NumericUpDown" | "DatePicker" | "TimePicker" | "Slider" | "ProgressBar" | "Separator" |
    "Image" | "TabControl" | "TabItem" | "Menu" | "MenuItem";
export type DesktopResourceValue = string | number | boolean | Thickness;
export interface DesktopResourceReference { resource: string; }
export type DesktopStyleValue = DesktopResourceValue | DesktopResourceReference;
export interface DesktopStyleSelector {
    control: DesktopControlKind;
    classes?: readonly string[];
}
export interface DesktopStyleSetters {
    width?: DesktopStyleValue; height?: DesktopStyleValue;
    minWidth?: DesktopStyleValue; minHeight?: DesktopStyleValue;
    maxWidth?: DesktopStyleValue; maxHeight?: DesktopStyleValue;
    opacity?: DesktopStyleValue; isVisible?: DesktopStyleValue; isEnabled?: DesktopStyleValue;
    margin?: DesktopStyleValue; padding?: DesktopStyleValue;
    background?: DesktopStyleValue; foreground?: DesktopStyleValue;
    fontSize?: DesktopStyleValue; fontWeight?: DesktopStyleValue; fontStyle?: DesktopStyleValue;
    horizontalAlignment?: DesktopStyleValue; verticalAlignment?: DesktopStyleValue;
}
export interface DesktopStyle {
    selector: DesktopStyleSelector;
    setters: DesktopStyleSetters;
}
export interface DesktopApplicationOptions {
    shutdownMode?: DesktopShutdownMode;
    onUnhandledError?: (error: unknown, window: DesktopWindow) => void;
    resources?: Readonly<{ [key: string]: DesktopResourceValue }>;
    styles?: readonly DesktopStyle[];
}
export interface DesktopWindowOptions {
    owner?: DesktopWindow;
    modal?: boolean;
    main?: boolean;
    onUnhandledError?: (error: unknown, window: DesktopWindow) => void;
}
export interface DesktopWindow extends DesktopRoot {
    readonly closed: Promise<void>;
    activate(): void;
    close(): void;
    findResource(key: string): DesktopResourceValue | null;
}
export interface DesktopApplication {
    readonly isDisposed: boolean;
    readonly windowCount: number;
    createWindow(element: GuiChild, options?: DesktopWindowOptions): DesktopWindow;
    shutdown(exitCode?: number): void;
    dispose(): void;
}

export function createDesktopApplication(options: DesktopApplicationOptions = {}): DesktopApplication {
    const managed: any = DesktopBridge.CreateDesktopApplication(options.shutdownMode || "onLastWindowClose");
    DesktopBridge.ConfigureDesktopStyleResources(managed, JSON.stringify({
        resources: options.resources || {},
        styles: options.styles || [],
    }));
    let application: DesktopApplication;
    application = {
        get isDisposed(): boolean { return managed.IsDisposed; },
        get windowCount(): number { return managed.WindowCount; },
        createWindow(element: GuiChild, windowOptions: DesktopWindowOptions = {}): DesktopWindow {
            if (managed.IsDisposed) throw new Error("The desktop application is disposed.");
            const owner: any = windowOptions.owner === undefined ? null : (windowOptions.owner as any).__managedRoot;
            if (windowOptions.owner !== undefined && owner === undefined)
                throw new Error("The owner must be a window from this desktop application.");
            let window: DesktopWindow;
            const report = (error: unknown): void => {
                const handler = windowOptions.onUnhandledError || options.onUnhandledError;
                if (handler === undefined) throw error;
                handler(error, window);
            };
            const runner = new ReactiveRoot(element, report);
            const root: any = DesktopBridge.CreateDesktopApplicationRoot(
                managed,
                (): void => runner.disposeFromManaged(),
                owner,
                windowOptions.modal === true,
                windowOptions.main === true);
            runner.setManaged(root);
            const closed = (async (): Promise<void> => { await root.Completion; })();
            window = {
                get isDisposed(): boolean { return root.IsDisposed; },
                closed,
                activate(): void { root.Activate(); },
                close(): void { root.Close(); },
                findResource(key: string): DesktopResourceValue | null {
                    return DesktopBridge.FindDesktopResource(managed, root, key) as any;
                },
                dispose(): void { root.Dispose(); },
            };
            (window as any).__managedRoot = root;
            try { runner.renderNow(); }
            catch (error) { root.Dispose(); throw error; }
            return window;
        },
        shutdown(exitCode: number = 0): void { managed.Shutdown(exitCode); },
        dispose(): void { managed.Dispose(); },
    };
    return application;
}

export type MessageDialogResult = "ok" | "cancel" | "yes" | "no";
export interface MessageDialogOptions { title?: string; message: string; buttons?: "ok" | "okCancel" | "yesNo"; }
export interface FileFilter { name: string; patterns: readonly string[]; }
export interface OpenFileDialogOptions { title?: string; allowMultiple?: boolean; filters?: readonly FileFilter[]; }
export interface SaveFileDialogOptions { title?: string; suggestedFileName?: string; defaultExtension?: string; filters?: readonly FileFilter[]; }
export interface FolderDialogOptions { title?: string; }
export async function showMessageDialog(options: MessageDialogOptions): Promise<MessageDialogResult> { return await DesktopBridge.ShowMessageDialogAsync(options.title || "", options.message, options.buttons || "ok") as any; }
export async function showOpenFileDialog(options: OpenFileDialogOptions = {}): Promise<string[]> { return await DesktopBridge.ShowOpenFileDialogAsync(options.title || "", options.allowMultiple === true, JSON.stringify(options.filters || [])) as any; }
export async function showSaveFileDialog(options: SaveFileDialogOptions = {}): Promise<string | null> { return await DesktopBridge.ShowSaveFileDialogAsync(options.title || "", options.suggestedFileName || "", options.defaultExtension || "", JSON.stringify(options.filters || [])) as any; }
export async function showFolderDialog(options: FolderDialogOptions = {}): Promise<string | null> { return await DesktopBridge.ShowFolderDialogAsync(options.title || "") as any; }
export async function readClipboardText(): Promise<string> { return await DesktopBridge.ReadClipboardTextAsync(); }
export async function writeClipboardText(value: string): Promise<void> { await DesktopBridge.WriteClipboardTextAsync(value); }
