// SharpTS minimal classic-mode react surface: just enough for `import React from "react"` +
// `React.createElement` (the default classic jsxFactory) to parse, check, and run without an
// npm install. Loading this module also brings the ambient JSX namespace into scope via the
// jsx-runtime import below. A real react in node_modules always wins over this shim.

import { jsx as _jsx, Fragment as _Fragment, isValidElement as _isValidElement } from "react/jsx-runtime";

export { Fragment, isValidElement } from "react/jsx-runtime";

// Minimal declaration-compatible component surface. The fallback is also used while
// checking the pinned TypeScript JSX corpus, whose classic React fixtures use the
// namespace import shape (`import React = require("react")`). Keep the public members
// structurally useful even though the runtime implementation remains intentionally tiny.
export class Component<P = {}, S = {}> {
    props: P;
    state: S;
    refs: any;

    constructor(props: P) {
        this.props = props;
        this.state = {} as S;
        this.refs = {};
    }

    setState(state: any): void {}

    forceUpdate(): void {}

    render(): any {
        return null;
    }
}

export class PureComponent<P = {}, S = {}> extends Component<P, S> {}

export type ReactElement<P = any> = JSX.Element;
export type StatelessComponent<P = {}> = (props: P) => JSX.Element | null;
export type SFC<P = {}> = StatelessComponent<P>;
export interface ComponentClass<P = {}> {
    new (props: P): Component<P, any>;
    defaultProps?: any;
}

export function createElement(type: any, props?: any, ...children: any[]): JSX.Element {
    const normalized: any = {};
    let key: any = undefined;
    if (props !== undefined && props !== null) {
        for (const name in props) {
            if (name === "key") {
                key = props[name];
                continue;
            }
            if (name === "ref") continue;
            normalized[name] = props[name];
        }
    }
    if (children.length === 1) {
        normalized["children"] = children[0];
    } else if (children.length > 1) {
        normalized["children"] = children;
    }
    return _jsx(type, normalized, key);
}

export const version: string = "18.3.0-sharpts";

// Deliberate deviation from the stdlib no-default-export rule (see CONTRIBUTING.md):
// `import React from "react"` is the dominant classic-mode idiom and must work without
// synthetic-default configuration.
const React = {
    createElement: createElement,
    Component: Component,
    PureComponent: PureComponent,
    Fragment: _Fragment,
    isValidElement: _isValidElement,
    version: version,
};
export default React;
