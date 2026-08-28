import { Fragment, createElement, createDevElement } from "./runtime";
import type { GuiElement } from "./runtime-types";

export { Fragment };

/**
 * Creates one GUI element for the automatic JSX production transform.
 * @param type - Function component or native GUI control tag.
 * @param props - Properties and children supplied to the element.
 * @param key - Optional reconciliation key supplied by the JSX transform.
 * @returns The immutable GUI element consumed by the desktop renderer.
 * @category JSX Runtime
 */
export function jsx(type: any, props: any, key?: any): GuiElement {
    return createElement(type, props, key);
}

/**
 * Creates a GUI element whose JSX children were emitted as a static group.
 * @param type - Function component or native GUI control tag.
 * @param props - Properties and children supplied to the element.
 * @param key - Optional reconciliation key supplied by the JSX transform.
 * @returns The immutable GUI element consumed by the desktop renderer.
 * @category JSX Runtime
 */
export const jsxs = jsx;

/**
 * Creates one GUI element for the automatic JSX development transform.
 * @param type - Function component or native GUI control tag.
 * @param props - Properties and children supplied to the element.
 * @param key - Optional reconciliation key supplied by the JSX transform.
 * @param _isStaticChildren - Development-transform marker indicating whether children are static.
 * @param source - Optional source location supplied by the development transform.
 * @param _self - Development-transform owner value reserved for JSX compatibility.
 * @returns The immutable GUI element, including its development source location when supplied.
 * @category JSX Runtime
 */
export function jsxDEV(
    type: any,
    props: any,
    key?: any,
    _isStaticChildren?: boolean,
    source?: any,
    _self?: any,
): GuiElement {
    return createDevElement(type, props, key, source);
}

declare global {
    namespace JSX {
        interface Element {
            readonly __guiElement: true;
            readonly type: any;
            readonly props: any;
            readonly key: string | null;
            readonly source: any;
        }
        interface ElementChildrenAttribute { children: {}; }
        interface IntrinsicAttributes { key?: string | number | null; }
    }
}
