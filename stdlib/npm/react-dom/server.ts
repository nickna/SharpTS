// SharpTS minimal server-side renderer for the shim's element objects
// ({ $$typeof, type, props, key } as produced by react/jsx-runtime and react's
// createElement). Escaping is XSS-safe by default; dangerouslySetInnerHTML is the only
// raw-output path. Fragments are detected structurally ($$typeof === "react.fragment"),
// never by reference, so elements from either the shim or a mixed setup render.

const VOID_ELEMENTS =
    " area base br col embed hr img input link meta param source track wbr ";

const UNITLESS_STYLE_PROPERTIES =
    " opacity zIndex flex flexGrow flexShrink fontWeight lineHeight order zoom " +
    "animationIterationCount columnCount fillOpacity strokeOpacity strokeDasharray ";

function isVoidElement(tag: string): boolean {
    return VOID_ELEMENTS.indexOf(" " + tag + " ") >= 0;
}

function escapeText(text: string): string {
    return text
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
}

function escapeAttribute(text: string): string {
    return escapeText(text).replace(/"/g, "&quot;");
}

function kebabCase(name: string): string {
    let out = "";
    for (let i = 0; i < name.length; i++) {
        const c = name[i];
        const lower = c.toLowerCase();
        if (c !== lower) {
            out += "-" + lower;
        } else {
            out += c;
        }
    }
    return out;
}

function styleToCss(style: any): string {
    if (typeof style === "string") return style;
    let css = "";
    for (const property in style) {
        const value = style[property];
        if (value === null || value === undefined || value === false) continue;
        let rendered: string;
        if (typeof value === "number" &&
            UNITLESS_STYLE_PROPERTIES.indexOf(" " + property + " ") < 0) {
            rendered = value + "px";
        } else {
            rendered = "" + value;
        }
        if (css.length > 0) css += ";";
        css += kebabCase(property) + ":" + rendered;
    }
    return css;
}

function renderAttributes(props: any): string {
    let out = "";
    for (const name in props) {
        if (name === "key" || name === "ref" || name === "children" ||
            name === "dangerouslySetInnerHTML") continue;
        const value = props[name];
        if (value === null || value === undefined || value === false) continue;
        if (typeof value === "function") continue;

        let attributeName = name;
        if (name === "className") attributeName = "class";
        else if (name === "htmlFor") attributeName = "for";

        if (value === true) {
            out += " " + attributeName;
            continue;
        }
        if (name === "style") {
            out += " style=\"" + escapeAttribute(styleToCss(value)) + "\"";
            continue;
        }
        out += " " + attributeName + "=\"" + escapeAttribute("" + value) + "\"";
    }
    return out;
}

export function renderToString(element: any): string {
    if (element === null || element === undefined || typeof element === "boolean") return "";
    if (typeof element === "string") return escapeText(element);
    if (typeof element === "number" || typeof element === "bigint") return escapeText("" + element);
    if (Array.isArray(element)) {
        let out = "";
        for (const child of element) out += renderToString(child);
        return out;
    }

    const type = element.type;
    const props: any = element.props === undefined || element.props === null ? {} : element.props;

    // Fragment (structural detection — see module header).
    if (type !== null && typeof type === "object" && type.$$typeof === "react.fragment") {
        return renderToString(props.children);
    }

    if (typeof type === "function") {
        // Basic class-component support: instances with a render method. Anything else is
        // treated as a function component.
        if (type.prototype !== undefined && type.prototype !== null &&
            typeof type.prototype.render === "function") {
            const instance = new type(props);
            return renderToString(instance.render());
        }
        return renderToString(type(props));
    }

    if (typeof type === "string") {
        let html = "<" + type + renderAttributes(props);
        if (isVoidElement(type)) {
            return html + "/>";
        }
        html += ">";
        const dangerous = props.dangerouslySetInnerHTML;
        if (dangerous !== undefined && dangerous !== null) {
            if (props.children !== undefined && props.children !== null) {
                throw new Error(
                    "Can only set one of `children` or `props.dangerouslySetInnerHTML`.");
            }
            html += "" + dangerous.__html;
        } else {
            html += renderToString(props.children);
        }
        return html + "</" + type + ">";
    }

    return "";
}

// Static markup differs from renderToString only in React-internal hydration markers,
// which this shim never emits.
export const renderToStaticMarkup = renderToString;
