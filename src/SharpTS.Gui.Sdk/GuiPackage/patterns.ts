import { createElement, useEffect, useRef } from "./runtime";
import type { DesktopWindow } from "./runtime";
import type { ControlRef, GuiElement, GuiChild, KeyEvent } from "./runtime-types";

/** A shared command for buttons, menus, and focus-aware shortcuts. @category Core and Composition */
export interface DesktopCommand {
    id: string;
    label: string;
    shortcut?: string;
    enabled?: boolean;
    checked?: boolean;
    /** Permit the command in editable inputs; false preserves native text editing. */
    allowInTextInput?: boolean;
    execute: () => void | Promise<unknown>;
}

/** Matches a normalized key event to a command, respecting input focus and modifiers. @category Core and Composition */
export function findCommand(commands: readonly DesktopCommand[], event: KeyEvent): DesktopCommand | null {
    for (const command of commands) {
        if (
            !command.shortcut ||
            command.enabled === false ||
            (event.isTextInput && !command.allowInTextInput)
        )
            continue;
        const parts = command.shortcut!.toLowerCase().split("+");
        const key = parts[parts.length - 1];
        if (
            event.key.toLowerCase() === key &&
            event.ctrl === parts.indexOf("ctrl") >= 0 &&
            event.alt === parts.indexOf("alt") >= 0 &&
            event.shift === parts.indexOf("shift") >= 0 &&
            event.meta === parts.indexOf("meta") >= 0
        )
            return command;
    }
    return null;
}

/** Native button props derived from a shared command. @category Core and Composition */
export function commandButton(command: DesktopCommand): {
    automationName: string;
    toolTip: string;
    isEnabled: boolean;
    onClick: () => void | Promise<unknown>;
} {
    return {
        automationName: command.label,
        toolTip: command.label + (command.shortcut ? " · " + command.shortcut : ""),
        isEnabled: command.enabled !== false,
        onClick: command.execute
    };
}

/** Native menu props derived from the same command as its button and shortcut. @category Core and Composition */
export function commandMenu(command: DesktopCommand): {
    header: string;
    inputGesture?: string;
    isEnabled: boolean;
    isChecked: boolean;
    onClick: () => void | Promise<unknown>;
} {
    return {
        header: command.label,
        inputGesture: command.shortcut,
        isEnabled: command.enabled !== false,
        isChecked: command.checked === true,
        onClick: command.execute
    };
}

/** Controls the result and initial focus of a native owned dialog. @category Application Lifecycle */
export interface DialogContext<T> {
    complete(value: T): void;
    cancel(): void;
}
/** Options for a native dialog. @category Application Lifecycle */
export interface DialogOptions<T> {
    title: string;
    width?: number;
    height?: number;
    initialFocus?: ControlRef<unknown>;
    content: (dialog: DialogContext<T>) => GuiChild;
}

/** Opens an owned modal window, resolves null on cancellation, and restores the owner's focus. @category Application Lifecycle */
interface ShowDialog {
    <T>(owner: DesktopWindow, options: DialogOptions<T>): Promise<T | null>;
}
function DialogFrame(props: { options: DialogOptions<any>; dialog: DialogContext<any> }): GuiElement {
    const options = props.options;
    useEffect(() => {
        if (options.initialFocus) options.initialFocus.focus();
    }, []);
    return createElement("Window", {
        title: options.title,
        width: options.width || 420,
        height: options.height || 280,
        canResize: false,
        theme: "system",
        keyDownRouting: "tunnel",
        onKeyDown: (event: KeyEvent): boolean => {
            if (event.key === "Escape") {
                props.dialog.cancel();
                return true;
            }
            return false;
        },
        children: options.content(props.dialog)
    });
}
function showDialogImpl(owner: DesktopWindow, options: DialogOptions<any>): Promise<any> {
    const restore = owner.captureFocus();

    return new Promise<any>((resolve, reject) => {
        let window: DesktopWindow | null = null;
        let result: any = null;
        const context: DialogContext<any> = {
            complete(value: any): void {
                result = value;
                if (window !== null) window.close();
            },
            cancel(): void {
                if (window !== null) window.close();
            }
        };
        try {
            window = owner.createOwnedWindow(createElement(DialogFrame, { options, dialog: context }), true);

            window.closed.then(() => {
                restore();
                resolve(result);
            }, reject);
        } catch (error) {
            restore();
            reject(error);
        }
    });
}

/** Opens a native owned modal dialog with a typed result. @category Application Lifecycle */
export const showDialog: ShowDialog = showDialogImpl as any;

/** A serialized application workflow such as Open/Save/Close. @category Hooks and State */
export interface SerialTask {
    readonly busy: boolean;
    run(work: () => Promise<void>): Promise<boolean>;
}
/** Prevents reentrant document workflows while allowing errors to reach the caller. @category Hooks and State */
export function createSerialTask(): SerialTask {
    let busy = false;
    async function run(work: () => Promise<void>): Promise<boolean> {
        if (busy) return false;
        busy = true;
        try {
            await work();
            return true;
        } finally {
            busy = false;
        }
    }
    return {
        get busy(): boolean {
            return busy;
        },
        run
    };
}

/** Guards asynchronous results and cancels the previous job on replacement or unmount. @category Hooks and State */
export function useLatestTask(): {
    begin(cancel?: () => void): number;
    isCurrent(id: number): boolean;
    cancel(): void;
} {
    const state = useRef({ generation: 0, alive: true, abort: null as (() => void) | null });
    const cancel = (): void => {
        state.current.generation++;
        if (state.current.abort !== null) state.current.abort();
        state.current.abort = null;
    };
    useEffect(() => {
        state.current.alive = true;
        return () => {
            state.current.alive = false;
            cancel();
        };
    }, []);
    return {
        begin(abort?: () => void): number {
            cancel();
            state.current.abort = abort || null;
            return state.current.generation;
        },
        isCurrent(id: number): boolean {
            return state.current.alive && state.current.generation === id;
        },
        cancel
    };
}

/** Calculates a zoom that fits content within a viewport, with space around its edges. @category Core and Composition */
export function fitZoom(
    width: number,
    height: number,
    viewportWidth: number,
    viewportHeight: number,
    padding: number = 24
): number {
    return Math.max(
        0.01,
        Math.min(1, (viewportWidth - padding * 2) / width, (viewportHeight - padding * 2) / height)
    );
}

/** Keeps the same content point under the pointer while zooming a scrollable surface. @category Core and Composition */
export function anchoredZoomOffset(
    offset: number,
    pointer: number,
    oldZoom: number,
    newZoom: number
): number {
    return Math.max(0, ((offset + pointer) * newZoom) / oldZoom - pointer);
}
