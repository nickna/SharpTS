namespace JSX {
    export interface Element { readonly element: true; }
    export interface ElementClass { render(): Element; }
    export interface ElementAttributesProperty { props: {}; }
    export interface ElementChildrenAttribute { content: {}; }
    export interface IntrinsicAttributes { key?: string | number; }
    export interface IntrinsicElements { div: { title?: string; content?: Element | Element[] }; }
}

declare function h(type: any, props: any, ...children: any[]): JSX.Element;

class ClassView {
    props!: { label: string; content?: JSX.Element };
    render(): JSX.Element { return <div title={this.props.label} />; }
}

function Generic<T>(props: { value: T; repeated: T; content?: JSX.Element }): JSX.Element {
    return <div title={String(props.value)} />;
}

interface CallableComponent { (props: { mode: "compact" | "full" }): JSX.Element; }
declare const Callable: CallableComponent;
declare function Overloaded(props: { kind: "text"; value: string }): JSX.Element;
declare function Overloaded(props: { kind: "number"; value: number }): JSX.Element;
declare function First(props: { first: string }): JSX.Element;
declare function Second(props: { second: number }): JSX.Element;
declare const UnionComponent: typeof First | typeof Second;

export const classComponent = <ClassView label="ok"><div /></ClassView>;
export const genericComponent = <Generic key="generic" value="a" repeated="b"><div /></Generic>;
export const callableObject = <Callable mode="compact" />;
export const overload = <Overloaded kind="number" value={2} />;
export const union = <UnionComponent first="accepted" />;
