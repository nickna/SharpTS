import { Fragment, createElement, createDevElement } from "./runtime";
import type { GuiElement } from "./runtime-types";

export { Fragment };

export function jsx(type: any, props: any, key?: any): GuiElement {
    return createElement(type, props, key);
}

export const jsxs = jsx;

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
