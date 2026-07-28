// SharpTS minimal JSX dev runtime (jsx: "react-jsxdev"). The dev transform passes extra
// source-location arguments that this shim ignores; behavior is identical to jsx-runtime.
// The bare specifier resolves back through the module chain, so the same react wins here
// as everywhere else.
export { Fragment, jsxDEV, isValidElement } from "react/jsx-runtime";
