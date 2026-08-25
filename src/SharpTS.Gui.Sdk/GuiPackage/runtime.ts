import { DesktopBridge } from "dotnet:SharpTS.Gui";
import type { GuiVNode } from "dotnet:SharpTS.Gui";
import type {
    CommonProps,
    ControlRef,
    CustomControlComponent,
    Dispatch,
    DropEffect,
    DropEvent,
    ErrorBoundaryProps,
    GuiChild,
    GuiElement,
    MutableRef,
    SelectionMode,
    SignalSetter,
    SourceInfo,
    StateSetter,
    Thickness,
} from "./runtime-types";
export * from "./control-surface.generated";

/** Groups adjacent GUI children without creating a native control. @category Core and Composition */
export const Fragment: any = "Fragment";
/**
 * Restores the previous native tree and renders fallback content after a descendant fails.
 * @param props - Protected children and the fallback renderer.
 * @returns The boundary element consumed by the GUI renderer.
 * @category Core and Composition
 */
export function ErrorBoundary(props: ErrorBoundaryProps): GuiElement { return props.children as any; }

function normalizeKey(key: any): string | null {
    return key === undefined || key === null ? null : String(key);
}
/** @internal */
export function createElement(type: any, props: any, key?: any): GuiElement {
    return { __guiElement: true, type, props: props === undefined || props === null ? {} : props, key: normalizeKey(key), source: null };
}
/** @internal */
export function createDevElement(type: any, props: any, key: any, source: any): GuiElement {
    const element = createElement(type, props, key);
    const info = source === undefined || source === null ? null : {
        fileName: source.fileName === undefined ? "" : source.fileName,
        lineNumber: source.lineNumber === undefined ? 0 : source.lineNumber,
        columnNumber: source.columnNumber === undefined ? 0 : source.columnNumber,
    };
    return { ...element, source: info };
}

/**
 * Creates a typed TSX tag for a statically packaged managed custom-control descriptor.
 * @typeParam P - Props accepted by the custom control.
 * @param kind - Provider-qualified descriptor name such as `vendor.widgets.Control`.
 * @returns A component value that creates the packaged control.
 * @category Core and Composition
 */
export function defineCustomControl<P extends object = {}>(kind: string): CustomControlComponent<P> {
    if (!/^[a-z][a-z0-9.-]*\.[A-Za-z][A-Za-z0-9]*$/.test(kind))
        throw new Error("Custom control kinds must use a provider-qualified name such as 'vendor.widgets.Control'.");
    return kind as any;
}

function customProperties(props: any): string {
    const copy: any = {};
    for (const name of Object.keys(props || {})) {
        if (name === "children" || name === "ref" || name === "key" || typeof props[name] === "function") continue;
        copy[name] = props[name];
    }
    return JSON.stringify(copy);
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
/**
 * Creates reactive state that invalidates every GUI root that reads it.
 * @typeParam T - Stored value type.
 * @param initial - Initial signal value.
 * @returns A getter and setter pair.
 * @category Hooks and State
 * @function
 */
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
/**
 * Stores component-local state between renders.
 * @typeParam T - Stored value type.
 * @param initial - Initial value or a lazy initializer.
 * @returns The current value and a state setter.
 * @category Hooks and State
 * @function
 */
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
/**
 * Stores component-local state updated by a reducer.
 * @typeParam S - State value type.
 * @typeParam A - Dispatched action type.
 * @param reducer - Produces the next state from the current state and an action.
 * @param initial - Initial reducer state.
 * @returns The current state and an action dispatcher.
 * @category Hooks and State
 * @function
 */
export const useReducer: UseReducer = useReducerImpl as any;

interface UseMemo { <T>(factory: () => T, deps: readonly unknown[]): T; }
function useMemoImpl(factory: any, deps: readonly unknown[]): any {
    const component = requireComponent("useMemo");
    const old: MemoHook | null = oldHook(component, "memo");
    const hook: MemoHook = old !== null && depsEqual(old.deps, deps) ? old : { kind: "memo", value: factory(), deps };
    appendHook(component, hook);
    return hook.value;
}
/**
 * Memoizes a computed value while its dependencies remain equal.
 * @typeParam T - Computed value type.
 * @param factory - Computes the value when dependencies change.
 * @param deps - Ordered dependency values.
 * @returns The memoized value.
 * @category Hooks and State
 * @function
 */
export const useMemo: UseMemo = useMemoImpl;
interface UseCallback { <T extends (...args: any[]) => any>(callback: T, deps: readonly unknown[]): T; }
function useCallbackImpl(callback: any, deps: readonly unknown[]): any { return useMemoImpl(() => callback, deps); }
/**
 * Preserves a callback identity while its dependencies remain equal.
 * @typeParam T - Callback type.
 * @param callback - Callback to retain.
 * @param deps - Ordered dependency values.
 * @returns The memoized callback.
 * @category Hooks and State
 * @function
 */
export const useCallback: UseCallback = useCallbackImpl;
interface UseRef { <T>(initial: T): MutableRef<T>; }
function useRefImpl(initial: any): MutableRef<any> {
    const component = requireComponent("useRef");
    const old: RefHook | null = oldHook(component, "ref");
    const hook: RefHook = old === null ? { kind: "ref", ref: { current: initial } } : old;
    appendHook(component, hook);
    return hook.ref;
}
/**
 * Creates a mutable value whose object identity remains stable between renders.
 * @typeParam T - Referenced value type.
 * @param initial - Initial current value.
 * @returns The stable mutable ref.
 * @category Hooks and State
 * @function
 */
export const useRef: UseRef = useRefImpl;
/**
 * Runs an effect after the native tree commits and cleans it up before rerun or unmount.
 * @param effect - Effect callback, optionally returning a cleanup callback.
 * @param deps - Optional dependency values controlling reruns.
 * @category Hooks and State
 */
export function useEffect(effect: () => any, deps?: readonly unknown[]): void {
    const component = requireComponent("useEffect");
    const old: EffectHook | null = oldHook(component, "effect");
    const changed = old === null || deps === undefined || !depsEqual(old.deps, deps);
    appendHook(component, { kind: "effect", effect, deps, cleanup: old === null ? null : old.cleanup, changed });
}

interface CreateControlRef { <THandle>(): ControlRef<THandle>; }
function createControlRefImpl(): any { return DesktopBridge.CreateRef(); }
/**
 * Creates a retained ref that can be passed to a control outside a component render.
 * @typeParam THandle - Expected native control handle type.
 * @returns A detached control ref.
 * @category Hooks and State
 * @function
 */
export const createControlRef: CreateControlRef = createControlRefImpl;
interface UseControlRef { <THandle>(): ControlRef<THandle>; }
function useControlRefImpl(): any {
    const component = requireComponent("useControlRef");
    const old: ControlRefHook | null = oldHook(component, "controlRef");
    const hook: ControlRefHook = old === null ? { kind: "controlRef", ref: createControlRefImpl() } : old;
    appendHook(component, hook);
    return hook.ref;
}
/**
 * Creates a retained control ref whose identity remains stable between renders.
 * @typeParam THandle - Expected native control handle type.
 * @returns The stable control ref.
 * @category Hooks and State
 * @function
 */
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
function dropEvent(files: string[], text: string | null, effect: DropEffect,
    ctrl: boolean, alt: boolean, shift: boolean, meta: boolean): DropEvent {
    return { files: files.slice(), text, effect, ctrl, alt, shift, meta };
}
function dragOverAction(handler: any): any {
    let result: any = null;
    if (typeof handler === "function") {
        result = (files: string[], text: string | null, effect: DropEffect,
            ctrl: boolean, alt: boolean, shift: boolean, meta: boolean): DropEffect =>
            handler(dropEvent(files, text, effect, ctrl, alt, shift, meta));
    }
    return result;
}
function dropAction(handler: any): any {
    let result: any = null;
    if (typeof handler === "function") {
        result = (files: string[], text: string | null, effect: DropEffect,
            ctrl: boolean, alt: boolean, shift: boolean, meta: boolean): void =>
            handler(dropEvent(files, text, effect, ctrl, alt, shift, meta));
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
        hasProperty(safe, "onKeyDown"), hasProperty(safe, "onKeyUp"),
        safe.allowDrop === true, dragOverAction(safe.onDragOver), dropAction(safe.onDrop),
        hasProperty(safe, "onDragOver"), hasProperty(safe, "onDrop")) as any;
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
            case "Window": node = DesktopBridge.CreateWindow(safe.title === undefined ? "SharpTS GUI" : safe.title, safe.width === undefined ? 720 : safe.width, safe.height === undefined ? 480 : safe.height, safe.canResize === undefined ? true : safe.canResize, safe.topmost === true, safe.theme === undefined ? "system" : safe.theme, children.nodes, key, ref); break;
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
            default:
                if (typeof element.type !== "string" || element.type.indexOf(".") < 1)
                    throw new Error("Unknown @sharpts/gui TSX tag: " + element.type);
                node = DesktopBridge.CreateCustomControl(
                    element.type, customProperties(safe), children.nodes, key, ref);
                break;
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

/** Stable string or number used to reconcile data-backed items. @category Data and Templates */
export type ItemKey = string | number;
/** Renders one item in a data-backed control. @category Data and Templates */
export type ItemTemplate<T> = (item: T, index: number) => GuiChild;
/** Props accepted by createVirtualList. @category Data and Templates */
export interface VirtualListProps<T> extends CommonProps<unknown> {
    /** Optional reconciliation key for the list control. */
    key?: ItemKey;
    /** Complete logical item collection. */
    items: readonly T[];
    /** Returns a stable key for an item and index. */
    itemKey: (item: T, index: number) => ItemKey;
    /** Renders one visible item. */
    renderItem: ItemTemplate<T>;
    /** Zero-based first visible logical item index. */
    startIndex: number;
    /** Number of logical items in the visible viewport. */
    visibleCount: number;
    /** Extra item count rendered before and after the viewport. */
    overscan?: number;
    /** Selected logical item indices. */
    selectedIndices?: readonly number[];
    /** Single- or multiple-selection behavior. */
    selectionMode?: SelectionMode;
    /** Called with logical item indices after selection changes. */
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
/**
 * Creates a virtualized native list from a logical item collection.
 * @typeParam T - Item value type.
 * @param props - Items, viewport range, key selector, and item renderer.
 * @returns A VirtualizingList element containing the visible item window.
 * @category Data and Templates
 * @function
 */
export const createVirtualList: CreateVirtualList = createVirtualListImpl;

/** Props accepted by createTree. @category Data and Templates */
export interface TreeProps<T> extends CommonProps<unknown> {
    /** Optional reconciliation key for the tree control. */
    key?: ItemKey;
    /** Root logical tree items. */
    items: readonly T[];
    /** Returns a stable key for an item. */
    itemKey: (item: T) => ItemKey;
    /** Returns the label displayed for an item. */
    itemLabel: (item: T) => string;
    /** Returns the logical children of an item. */
    childrenOf: (item: T) => readonly T[];
    /** Returns whether an item is expanded. */
    isExpanded?: (item: T) => boolean;
    /** Called after an item's expanded state changes. */
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
/**
 * Creates a native tree from hierarchical application data.
 * @typeParam T - Tree item value type.
 * @param props - Root items and callbacks describing the hierarchy.
 * @returns A TreeView element containing keyed TreeViewItem elements.
 * @category Data and Templates
 * @function
 */
export const createTree: CreateTree = createTreeImpl;

/** Column definition used by createVirtualDataGrid. @category Data and Templates */
export interface DataGridColumn<T> {
    /** Stable column key. */
    key: string;
    /** Header text displayed above the column. */
    header: string;
    /** Renders the cell for one logical row. */
    renderCell: (item: T, rowIndex: number) => GuiChild;
}
/** Props accepted by createVirtualDataGrid. @category Data and Templates */
export interface VirtualDataGridProps<T> extends CommonProps<unknown> {
    /** Optional reconciliation key for the grid control. */
    key?: ItemKey;
    /** Complete logical row collection. */
    items: readonly T[];
    /** Ordered grid column definitions. */
    columns: readonly DataGridColumn<T>[];
    /** Returns a stable key for a row value and index. */
    rowKey: (item: T, rowIndex: number) => ItemKey;
    /** Zero-based first visible logical row index. */
    startIndex: number;
    /** Number of logical rows in the visible viewport. */
    visibleCount: number;
    /** Extra row count rendered before and after the viewport. */
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
/**
 * Creates a virtualized grid from logical rows and column templates.
 * @typeParam T - Row value type.
 * @param props - Rows, columns, viewport range, and row key selector.
 * @returns A Grid element containing the visible cells.
 * @category Data and Templates
 * @function
 */
export const createVirtualDataGrid: CreateVirtualDataGrid = createVirtualDataGridImpl;

/** Condition that ends the desktop application message loop. @category Application Lifecycle */
export type DesktopShutdownMode = "onLastWindowClose" | "onMainWindowClose" | "explicit";
/** Built-in native control kind that can be targeted by application styles. @category Core and Composition */
export type DesktopControlKind =
    "Control" | "Window" | "StackPanel" | "ToolBar" | "WrapPanel" | "DockPanel" | "Grid" |
    "Border" | "StatusBar" | "ScrollViewer" | "TextBlock" | "Button" | "TextBox" |
    "PasswordBox" | "CheckBox" | "RadioButton" | "ToggleSwitch" | "ComboBox" | "ListBox" |
    "NumericUpDown" | "DatePicker" | "TimePicker" | "Slider" | "ProgressBar" | "Separator" |
    "Image" | "TabControl" | "TabItem" | "Menu" | "MenuItem";
/** Literal value stored in an application resource dictionary. @category Core and Composition */
export type DesktopResourceValue = string | number | boolean | Thickness;
/** Reference to a named application resource. @category Core and Composition */
export interface DesktopResourceReference {
    /** Resource dictionary key resolved when the style is applied. */
    resource: string;
}
/** Literal or resource-backed value assigned by a desktop style. @category Core and Composition */
export type DesktopStyleValue = DesktopResourceValue | DesktopResourceReference;
/** Selects controls by native kind and optional style classes. @category Core and Composition */
export interface DesktopStyleSelector {
    /** Native control kind matched by the selector. */
    control: DesktopControlKind;
    /** Style classes that must all be present. */
    classes?: readonly string[];
}
/** Values assigned when a DesktopStyle selector matches a control. @category Core and Composition */
export interface DesktopStyleSetters {
    /** Preferred control width. */
    width?: DesktopStyleValue;
    /** Preferred control height. */
    height?: DesktopStyleValue;
    /** Minimum control width. */
    minWidth?: DesktopStyleValue;
    /** Minimum control height. */
    minHeight?: DesktopStyleValue;
    /** Maximum control width. */
    maxWidth?: DesktopStyleValue;
    /** Maximum control height. */
    maxHeight?: DesktopStyleValue;
    /** Control opacity. */
    opacity?: DesktopStyleValue;
    /** Control visibility. */
    isVisible?: DesktopStyleValue;
    /** Control enabled state. */
    isEnabled?: DesktopStyleValue;
    /** Space outside the control. */
    margin?: DesktopStyleValue;
    /** Space inside the control border. */
    padding?: DesktopStyleValue;
    /** Background brush or color. */
    background?: DesktopStyleValue;
    /** Foreground brush or color. */
    foreground?: DesktopStyleValue;
    /** Text font size. */
    fontSize?: DesktopStyleValue;
    /** Text font weight. */
    fontWeight?: DesktopStyleValue;
    /** Text font style. */
    fontStyle?: DesktopStyleValue;
    /** Horizontal layout alignment. */
    horizontalAlignment?: DesktopStyleValue;
    /** Vertical layout alignment. */
    verticalAlignment?: DesktopStyleValue;
}
/** One application-level style rule. @category Core and Composition */
export interface DesktopStyle {
    /** Controls matched by the rule. */
    selector: DesktopStyleSelector;
    /** Values assigned to matched controls. */
    setters: DesktopStyleSetters;
}
/** Options used to create a desktop application. @category Application Lifecycle */
export interface DesktopApplicationOptions {
    /** Condition that ends the application message loop. */
    shutdownMode?: DesktopShutdownMode;
    /** Handles render, effect, and native commit failures not caught by an ErrorBoundary. */
    onUnhandledError?: (error: unknown, window: DesktopWindow) => void;
    /** Named values available to window and control resource lookup. */
    resources?: Readonly<{ [key: string]: DesktopResourceValue }>;
    /** Application-level native control styles. */
    styles?: readonly DesktopStyle[];
}
/** Options used when creating a desktop window. @category Application Lifecycle */
export interface DesktopWindowOptions {
    /** Owner window used for native parent and modality behavior. */
    owner?: DesktopWindow;
    /** Whether the window blocks interaction with its owner until closed. */
    modal?: boolean;
    /** Whether this window is the application's main window. */
    main?: boolean;
    /** Window-specific unhandled error callback. */
    onUnhandledError?: (error: unknown, window: DesktopWindow) => void;
}
/** Live desktop window created by DesktopApplication. @category Application Lifecycle */
export interface DesktopWindow {
    /** Whether the native window and reactive root have been disposed. */
    readonly isDisposed: boolean;
    /** Promise completed after the native window closes. */
    readonly closed: Promise<void>;
    /** Activates the window and requests foreground focus. */
    activate(): void;
    /** Requests that the native window close. */
    close(): void;
    /** Looks up a resource through the window and application dictionaries. @returns The resource value, or null when not found. */
    findResource(key: string): DesktopResourceValue | null;
    /** Disposes the native window and its reactive tree. */
    dispose(): void;
}
/** Native tray menu entry. @category Desktop Services */
export interface TrayMenuItem {
    /** Identifier reported when this menu item is activated. */
    id?: string;
    /** Text displayed for the item. */
    label?: string;
    /** Whether this entry is a visual separator. */
    separator?: boolean;
    /** Whether the item accepts interaction. */
    isEnabled?: boolean;
    /** Whether the item displays a checked state. */
    isChecked?: boolean;
}
/** Options used to create or update a native tray icon. @category Desktop Services */
export interface TrayIconOptions {
    /** Application asset path for the tray icon image. */
    icon: string;
    /** Hover text displayed by the desktop shell. */
    toolTip?: string;
    /** Native context-menu entries. */
    menu?: readonly TrayMenuItem[];
    /** Called when the tray icon itself is activated. */
    onClick?: () => void;
    /** Called with an item identifier when a tray menu entry is activated. */
    onMenuItemClick?: (id: string) => void;
}
/** Live native tray icon owned by a DesktopApplication. @category Desktop Services */
export interface DesktopTrayIcon {
    /** Whether the native tray icon has been disposed. */
    readonly isDisposed: boolean;
    /** Replaces the icon image, tooltip, menu, and callbacks. */
    update(options: TrayIconOptions): void;
    /** Removes and disposes the native tray icon. */
    dispose(): void;
}
/** Desktop application lifetime and window manager. @category Application Lifecycle */
export interface DesktopApplication {
    /** Whether the application lifetime has been disposed. */
    readonly isDisposed: boolean;
    /** Number of currently open application windows. */
    readonly windowCount: number;
    /** Creates and renders a native window. @returns The live desktop window. */
    createWindow(element: GuiChild, options?: DesktopWindowOptions): DesktopWindow;
    /** Creates a native system tray icon. @returns The live tray icon. */
    createTrayIcon(options: TrayIconOptions): DesktopTrayIcon;
    /** Ends the desktop message loop with an optional process exit code. */
    shutdown(exitCode?: number): void;
    /** Disposes all windows, tray icons, and application resources. */
    dispose(): void;
}

/**
 * Creates the desktop application lifetime used to open windows and tray icons.
 * @param options - Shutdown, error handling, resource, and style options.
 * @returns The live desktop application.
 * @category Application Lifecycle
 */
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
        createTrayIcon(trayOptions: TrayIconOptions): DesktopTrayIcon {
            if (managed.IsDisposed) throw new Error("The desktop application is disposed.");
            let current = trayOptions;
            const create = (options: TrayIconOptions): any => DesktopBridge.CreateDesktopTrayIcon(
                managed,
                options.icon,
                options.toolTip || "",
                JSON.stringify(options.menu || []),
                action(options.onClick),
                stringAction(options.onMenuItemClick));
            const native: any = create(current);
            return {
                get isDisposed(): boolean { return native.IsDisposed; },
                update(options: TrayIconOptions): void {
                    current = options;
                    native.Update(
                        current.icon,
                        current.toolTip || "",
                        JSON.stringify(current.menu || []),
                        action(current.onClick),
                        stringAction(current.onMenuItemClick));
                },
                dispose(): void { native.Dispose(); },
            };
        },
        shutdown(exitCode: number = 0): void { managed.Shutdown(exitCode); },
        dispose(): void { managed.Dispose(); },
    };
    return application;
}

/** Button selected when a native message dialog closes. @category Desktop Services */
export type MessageDialogResult = "ok" | "cancel" | "yes" | "no";
/** Options for showMessageDialog. @category Desktop Services */
export interface MessageDialogOptions {
    /** Native dialog title. */
    title?: string;
    /** Message displayed in the dialog body. */
    message: string;
    /** Set of native buttons displayed by the dialog. */
    buttons?: "ok" | "okCancel" | "yesNo";
}
/** Named file-extension pattern group used by native file dialogs. @category Desktop Services */
export interface FileFilter {
    /** User-facing filter name. */
    name: string;
    /** File patterns such as `*.png` accepted by the filter. */
    patterns: readonly string[];
}
/** Options for showOpenFileDialog. @category Desktop Services */
export interface OpenFileDialogOptions {
    /** Native dialog title. */
    title?: string;
    /** Whether the user can choose multiple files. */
    allowMultiple?: boolean;
    /** File filters offered by the dialog. */
    filters?: readonly FileFilter[];
}
/** Options for showSaveFileDialog. @category Desktop Services */
export interface SaveFileDialogOptions {
    /** Native dialog title. */
    title?: string;
    /** File name initially suggested to the user. */
    suggestedFileName?: string;
    /** Extension appended when the user omits one. */
    defaultExtension?: string;
    /** File filters offered by the dialog. */
    filters?: readonly FileFilter[];
}
/** Options for showFolderDialog. @category Desktop Services */
export interface FolderDialogOptions {
    /** Native dialog title. */
    title?: string;
}
/** Operating-system and well-known directory information. @category Desktop Services */
export interface DesktopPlatformInfo {
    /** Normalized operating-system identifier. */
    operatingSystem: "windows" | "macos" | "linux" | "unknown";
    /** Runtime process architecture. */
    architecture: string;
    /** Managed runtime framework description. */
    framework: string;
    /** Directory containing the running application. */
    applicationDirectory: string;
    /** Per-user local application data directory. */
    localApplicationData: string;
    /** Per-user roaming application data directory. */
    roamingApplicationData: string;
    /** User documents directory. */
    documents: string;
    /** User desktop directory. */
    desktop: string;
    /** Operating-system temporary directory. */
    temporaryDirectory: string;
}
/** Rectangle in desktop coordinate space. @category Desktop Services */
export interface DesktopDisplayBounds {
    /** Horizontal origin in device-independent pixels. */
    x: number;
    /** Vertical origin in device-independent pixels. */
    y: number;
    /** Rectangle width in device-independent pixels. */
    width: number;
    /** Rectangle height in device-independent pixels. */
    height: number;
}
/** Native desktop display information. @category Desktop Services */
export interface DesktopDisplayInfo {
    /** Operating-system display name. */
    name: string;
    /** Whether this is the primary display. */
    isPrimary: boolean;
    /** Native scaling factor. */
    scaling: number;
    /** Current native display orientation. */
    orientation: "landscape" | "portrait" | "landscapeflipped" | "portraitflipped" | "none";
    /** Full display bounds. */
    bounds: DesktopDisplayBounds;
    /** Bounds remaining after desktop shell work areas are excluded. */
    workingArea: DesktopDisplayBounds;
}
/** Options for showNotification. @category Desktop Services */
export interface DesktopNotificationOptions {
    /** Notification title. */
    title: string;
    /** Optional notification body. */
    message?: string;
    /** Whether notification sounds are suppressed. */
    silent?: boolean;
}
/**
 * Displays a native message dialog.
 * @param options - Title, message, and button-set options.
 * @returns The button selected by the user.
 * @category Desktop Services
 */
export async function showMessageDialog(options: MessageDialogOptions): Promise<MessageDialogResult> { return await DesktopBridge.ShowMessageDialogAsync(options.title || "", options.message, options.buttons || "ok") as any; }
/**
 * Displays a native open-file dialog.
 * @param options - Title, multi-select, and filter options.
 * @returns The selected file paths, or an empty array when canceled.
 * @category Desktop Services
 */
export async function showOpenFileDialog(options: OpenFileDialogOptions = {}): Promise<string[]> { return await DesktopBridge.ShowOpenFileDialogAsync(options.title || "", options.allowMultiple === true, JSON.stringify(options.filters || [])) as any; }
/**
 * Displays a native save-file dialog.
 * @param options - Title, suggested name, extension, and filter options.
 * @returns The selected file path, or null when canceled.
 * @category Desktop Services
 */
export async function showSaveFileDialog(options: SaveFileDialogOptions = {}): Promise<string | null> { return await DesktopBridge.ShowSaveFileDialogAsync(options.title || "", options.suggestedFileName || "", options.defaultExtension || "", JSON.stringify(options.filters || [])) as any; }
/**
 * Displays a native folder-selection dialog.
 * @param options - Dialog title options.
 * @returns The selected folder path, or null when canceled.
 * @category Desktop Services
 */
export async function showFolderDialog(options: FolderDialogOptions = {}): Promise<string | null> { return await DesktopBridge.ShowFolderDialogAsync(options.title || "") as any; }
/** Reads plain text from the desktop clipboard. @returns Clipboard text, or an empty string when no text is available. @category Desktop Services */
export async function readClipboardText(): Promise<string> { return await DesktopBridge.ReadClipboardTextAsync(); }
/**
 * Writes plain text to the desktop clipboard.
 * @param value - Text to place on the clipboard.
 * @returns A promise completed after the clipboard is updated.
 * @category Desktop Services
 */
export async function writeClipboardText(value: string): Promise<void> { await DesktopBridge.WriteClipboardTextAsync(value); }
/** Returns command-line arguments supplied when the desktop application launched. @returns Launch argument strings. @category Desktop Services */
export function getLaunchArguments(): string[] { return DesktopBridge.GetDesktopLaunchArguments() as any; }
/** Returns operating-system, runtime, and well-known directory information. @returns Current desktop platform information. @category Desktop Services */
export function getDesktopPlatformInfo(): DesktopPlatformInfo { return JSON.parse(DesktopBridge.GetDesktopPlatformInfoJson()) as DesktopPlatformInfo; }
/** Returns information about connected desktop displays. @returns Connected display records. @category Desktop Services */
export function getDesktopDisplays(): DesktopDisplayInfo[] { return JSON.parse(DesktopBridge.GetDesktopDisplaysJson()) as DesktopDisplayInfo[]; }
/**
 * Opens a URI or file with the operating system's default application.
 * @param target - URI or file path to open.
 * @returns A promise completed after the native open request is submitted.
 * @category Desktop Services
 */
export async function openExternal(target: string): Promise<void> { await DesktopBridge.OpenDesktopExternalAsync(target); }
/**
 * Reveals a file or folder in the native file manager.
 * @param path - Local path to reveal.
 * @returns A promise completed after the native reveal request is submitted.
 * @category Desktop Services
 */
export async function showItemInFolder(path: string): Promise<void> { await DesktopBridge.ShowDesktopItemInFolderAsync(path); }
/**
 * Sends a local file to the operating system's print workflow.
 * @param path - Local file path to print.
 * @returns A promise completed after the native print request is submitted.
 * @category Desktop Services
 */
export async function printFile(path: string): Promise<void> { await DesktopBridge.PrintDesktopFileAsync(path); }
/**
 * Displays a native desktop notification.
 * @param options - Notification title, body, and sound options.
 * @returns A promise completed after the notification request is submitted.
 * @category Desktop Services
 */
export function showNotification(options: DesktopNotificationOptions): Promise<void> {
    return DesktopBridge.ShowDesktopNotificationAsync(options.title, options.message || "", options.silent === true) as any;
}
