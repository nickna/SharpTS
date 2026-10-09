/** @jsxImportSource react */
import{renderToString}from "react-dom/server";
// formatter-comment: JSX whitespace, entities, attributes and embedded expressions are observable.
const view=<p title="it's &amp; that">don't stop — 🧭 &lt;tags&gt;</p>;
console.log(renderToString(view));
const number=7;console.log(renderToString(<p>{"value:"}{number}</p>));
