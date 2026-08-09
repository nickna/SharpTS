namespace JSX {
    export interface Element { readonly element: true; }
    export interface ElementClass { render(): Element; }
    export interface ElementAttributesProperty { props: {}; }
    export interface ElementChildrenAttribute { content: {}; }
    export interface IntrinsicAttributes { key?: string | number; }
    export interface IntrinsicElements { div: { title?: string; content?: Element | Element[] }; }
}

declare function h(type: any, props: any, ...children: any[]): JSX.Element;

class BadClass {
    props!: { required: string };
}
class ChildClass {
    props!: { content: JSX.Element };
    render(): JSX.Element { return <div />; }
}
async function AsyncComponent(): Promise<JSX.Element> { return <div />; }
declare function Good(props: { value: string }): JSX.Element;
declare const BadUnion: typeof Good | number;
interface CallableComponent { (props: { mode: "compact" | "full" }): JSX.Element; }
declare const Callable: CallableComponent;

export const invalidClassContract = <BadClass required="x" />;
export const invalidClassProps = <ChildClass><div /><div /></ChildClass>;
export const invalidAsync = <AsyncComponent />;
export const invalidUnion = <BadUnion value="x" />;
export const invalidCallableProps = <Callable mode="unknown" />;
