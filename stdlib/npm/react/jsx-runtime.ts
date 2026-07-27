// SharpTS minimal JSX automatic runtime.
//
// Loaded for the synthesized `import { jsx } from "react/jsx-runtime"` the parser injects in
// automatic jsx modes when no real `react` package resolves from node_modules. Produces plain
// element objects `{ $$typeof, type, props, key }` that `react-dom/server`'s renderToString
// understands. This module is also the canonical home of the ambient JSX namespace: the
// `declare global` block below merges into the program's global scope only when this module
// is actually loaded, so programs without JSX see no JSX namespace (matching tsc).
//
// Fragment is a tagged object rather than a Symbol so identity survives every execution mode;
// consumers must compare structurally (`type.$$typeof === "react.fragment"`), never by
// reference across packages.

export const Fragment: any = { $$typeof: "react.fragment" };

export function jsx(type: any, props: any, key?: any): JSX.Element {
    const normalized: any = {};
    if (props !== undefined && props !== null) {
        for (const name in props) {
            if (name === "key" || name === "ref") continue;
            normalized[name] = props[name];
        }
    }
    return {
        $$typeof: "react.element",
        type: type,
        props: normalized,
        key: key === undefined || key === null ? null : "" + key,
    } as any;
}

// Static-children variant: same runtime behavior, the distinction only matters to renderers
// that key-warn (which this shim does not).
export const jsxs = jsx;

export function jsxDEV(
    type: any,
    props: any,
    key?: any,
    _isStaticChildren?: boolean,
    _source?: any,
    _self?: any,
): JSX.Element {
    return jsx(type, props, key);
}

export function isValidElement(value: any): boolean {
    return value !== null && typeof value === "object" && value.$$typeof === "react.element";
}

// ---------------------------------------------------------------------------------------------
// Ambient JSX types. HARD RULE: this block must be CLOSED — every type it references must be
// declared inside the block or be a global built-in. It merges in the global environment,
// where module-local names do not resolve (they would silently become `any`).
//
// IntrinsicElements deliberately has NO string index signature: an unknown tag (`<dvi>`) must
// stay a checkable error. Unknown *attributes* are tolerated through the checker's hyphen
// exemption (data-*/aria-*) and the generously optional members below.
// ---------------------------------------------------------------------------------------------
declare global {
    namespace JSX {
        interface Element {
            $$typeof: any;
            type: any;
            props: any;
            key: string | number | null;
        }

        interface ElementClass {
            render(): any;
        }

        interface ElementAttributesProperty {
            props: {};
        }

        interface ElementChildrenAttribute {
            children: {};
        }

        interface IntrinsicAttributes {
            key?: string | number | bigint | null;
        }

        interface SharpTSDOMAttributes {
            children?: any;
            className?: string;
            id?: string;
            style?: string | { [property: string]: string | number };
            title?: string;
            role?: string;
            tabIndex?: number;
            hidden?: boolean;
            slot?: string;
            lang?: string;
            dir?: string;
            draggable?: boolean;
            contentEditable?: boolean | "true" | "false" | "inherit" | "plaintext-only";
            accessKey?: string;
            dangerouslySetInnerHTML?: { __html: string };
            onClick?: (event: any) => void;
            onDoubleClick?: (event: any) => void;
            onChange?: (event: any) => void;
            onInput?: (event: any) => void;
            onSubmit?: (event: any) => void;
            onReset?: (event: any) => void;
            onKeyDown?: (event: any) => void;
            onKeyUp?: (event: any) => void;
            onKeyPress?: (event: any) => void;
            onFocus?: (event: any) => void;
            onBlur?: (event: any) => void;
            onMouseDown?: (event: any) => void;
            onMouseUp?: (event: any) => void;
            onMouseEnter?: (event: any) => void;
            onMouseLeave?: (event: any) => void;
            onMouseMove?: (event: any) => void;
            onMouseOver?: (event: any) => void;
            onMouseOut?: (event: any) => void;
            onWheel?: (event: any) => void;
            onScroll?: (event: any) => void;
            onLoad?: (event: any) => void;
            onError?: (event: any) => void;
            onTouchStart?: (event: any) => void;
            onTouchEnd?: (event: any) => void;
            onTouchMove?: (event: any) => void;
            onPointerDown?: (event: any) => void;
            onPointerUp?: (event: any) => void;
            onDrag?: (event: any) => void;
            onDragStart?: (event: any) => void;
            onDragEnd?: (event: any) => void;
            onDragOver?: (event: any) => void;
            onDrop?: (event: any) => void;
            onContextMenu?: (event: any) => void;
            onAnimationEnd?: (event: any) => void;
            onTransitionEnd?: (event: any) => void;
        }

        interface SharpTSAnchorAttributes extends SharpTSDOMAttributes {
            href?: string;
            target?: string;
            rel?: string;
            download?: any;
            hreflang?: string;
            type?: string;
            referrerPolicy?: string;
        }

        interface SharpTSImageAttributes extends SharpTSDOMAttributes {
            src?: string;
            alt?: string;
            width?: number | string;
            height?: number | string;
            loading?: "eager" | "lazy";
            srcSet?: string;
            sizes?: string;
            crossOrigin?: string;
            decoding?: "async" | "auto" | "sync";
            referrerPolicy?: string;
        }

        interface SharpTSInputAttributes extends SharpTSDOMAttributes {
            type?: string;
            value?: string | number;
            defaultValue?: string | number;
            checked?: boolean;
            defaultChecked?: boolean;
            placeholder?: string;
            disabled?: boolean;
            name?: string;
            required?: boolean;
            readOnly?: boolean;
            min?: number | string;
            max?: number | string;
            step?: number | string;
            minLength?: number;
            maxLength?: number;
            pattern?: string;
            autoComplete?: string;
            autoFocus?: boolean;
            multiple?: boolean;
            accept?: string;
            form?: string;
            list?: string;
            size?: number;
        }

        interface SharpTSButtonAttributes extends SharpTSDOMAttributes {
            type?: "button" | "submit" | "reset";
            disabled?: boolean;
            name?: string;
            value?: string | number;
            form?: string;
            autoFocus?: boolean;
        }

        interface SharpTSLabelAttributes extends SharpTSDOMAttributes {
            htmlFor?: string;
            form?: string;
        }

        interface SharpTSFormAttributes extends SharpTSDOMAttributes {
            action?: string;
            method?: string;
            encType?: string;
            target?: string;
            noValidate?: boolean;
            autoComplete?: string;
            name?: string;
        }

        interface SharpTSSelectAttributes extends SharpTSDOMAttributes {
            value?: string | number;
            defaultValue?: string | number;
            disabled?: boolean;
            multiple?: boolean;
            name?: string;
            required?: boolean;
            size?: number;
            autoFocus?: boolean;
            form?: string;
        }

        interface SharpTSOptionAttributes extends SharpTSDOMAttributes {
            value?: string | number;
            selected?: boolean;
            disabled?: boolean;
            label?: string;
        }

        interface SharpTSOptgroupAttributes extends SharpTSDOMAttributes {
            disabled?: boolean;
            label?: string;
        }

        interface SharpTSTextareaAttributes extends SharpTSDOMAttributes {
            value?: string;
            defaultValue?: string;
            rows?: number;
            cols?: number;
            placeholder?: string;
            disabled?: boolean;
            name?: string;
            required?: boolean;
            readOnly?: boolean;
            wrap?: string;
            maxLength?: number;
            minLength?: number;
            autoFocus?: boolean;
        }

        interface SharpTSTableCellAttributes extends SharpTSDOMAttributes {
            colSpan?: number;
            rowSpan?: number;
            headers?: string;
            scope?: string;
            abbr?: string;
        }

        interface SharpTSIframeAttributes extends SharpTSDOMAttributes {
            src?: string;
            srcDoc?: string;
            width?: number | string;
            height?: number | string;
            name?: string;
            sandbox?: string;
            allow?: string;
            allowFullScreen?: boolean;
            loading?: "eager" | "lazy";
            referrerPolicy?: string;
        }

        interface SharpTSMediaAttributes extends SharpTSDOMAttributes {
            src?: string;
            controls?: boolean;
            autoPlay?: boolean;
            loop?: boolean;
            muted?: boolean;
            preload?: "none" | "metadata" | "auto" | "";
            crossOrigin?: string;
        }

        interface SharpTSVideoAttributes extends SharpTSMediaAttributes {
            width?: number | string;
            height?: number | string;
            poster?: string;
            playsInline?: boolean;
        }

        interface SharpTSSourceAttributes extends SharpTSDOMAttributes {
            src?: string;
            srcSet?: string;
            type?: string;
            media?: string;
            sizes?: string;
        }

        interface SharpTSTrackAttributes extends SharpTSDOMAttributes {
            src?: string;
            kind?: string;
            srcLang?: string;
            label?: string;
            default?: boolean;
        }

        interface SharpTSCanvasAttributes extends SharpTSDOMAttributes {
            width?: number | string;
            height?: number | string;
        }

        interface SharpTSMetaAttributes extends SharpTSDOMAttributes {
            name?: string;
            content?: string;
            charSet?: string;
            httpEquiv?: string;
            property?: string;
        }

        interface SharpTSLinkAttributes extends SharpTSDOMAttributes {
            href?: string;
            rel?: string;
            type?: string;
            media?: string;
            sizes?: string;
            as?: string;
            crossOrigin?: string;
            integrity?: string;
        }

        interface SharpTSScriptAttributes extends SharpTSDOMAttributes {
            src?: string;
            type?: string;
            async?: boolean;
            defer?: boolean;
            crossOrigin?: string;
            integrity?: string;
            noModule?: boolean;
            nonce?: string;
        }

        interface SharpTSStyleAttributes extends SharpTSDOMAttributes {
            type?: string;
            media?: string;
            nonce?: string;
        }

        interface SharpTSTimeAttributes extends SharpTSDOMAttributes {
            dateTime?: string;
        }

        interface SharpTSProgressAttributes extends SharpTSDOMAttributes {
            value?: number | string;
            max?: number | string;
        }

        interface SharpTSMeterAttributes extends SharpTSDOMAttributes {
            value?: number | string;
            min?: number | string;
            max?: number | string;
            low?: number;
            high?: number;
            optimum?: number;
        }

        interface SharpTSOlAttributes extends SharpTSDOMAttributes {
            start?: number;
            reversed?: boolean;
            type?: string;
        }

        interface SharpTSLiAttributes extends SharpTSDOMAttributes {
            value?: number;
        }

        interface SharpTSQuoteAttributes extends SharpTSDOMAttributes {
            cite?: string;
        }

        interface SharpTSModAttributes extends SharpTSDOMAttributes {
            cite?: string;
            dateTime?: string;
        }

        interface SharpTSDetailsAttributes extends SharpTSDOMAttributes {
            open?: boolean;
        }

        interface SharpTSDialogAttributes extends SharpTSDOMAttributes {
            open?: boolean;
        }

        interface SharpTSDataAttributes extends SharpTSDOMAttributes {
            value?: string | number;
        }

        interface SharpTSOutputAttributes extends SharpTSDOMAttributes {
            htmlFor?: string;
            form?: string;
            name?: string;
        }

        interface SharpTSFieldsetAttributes extends SharpTSDOMAttributes {
            disabled?: boolean;
            form?: string;
            name?: string;
        }

        interface SharpTSColAttributes extends SharpTSDOMAttributes {
            span?: number;
        }

        interface SharpTSAreaAttributes extends SharpTSDOMAttributes {
            alt?: string;
            coords?: string;
            shape?: string;
            href?: string;
            target?: string;
            rel?: string;
            download?: any;
        }

        interface SharpTSBaseAttributes extends SharpTSDOMAttributes {
            href?: string;
            target?: string;
        }

        interface SharpTSEmbedAttributes extends SharpTSDOMAttributes {
            src?: string;
            type?: string;
            width?: number | string;
            height?: number | string;
        }

        interface SharpTSObjectAttributes extends SharpTSDOMAttributes {
            data?: string;
            type?: string;
            name?: string;
            width?: number | string;
            height?: number | string;
            form?: string;
        }

        interface SharpTSParamAttributes extends SharpTSDOMAttributes {
            name?: string;
            value?: string | number;
        }

        interface SharpTSMapAttributes extends SharpTSDOMAttributes {
            name?: string;
        }

        interface SharpTSHtmlAttributes extends SharpTSDOMAttributes {
            xmlns?: string;
        }

        interface SharpTSSlotAttributes extends SharpTSDOMAttributes {
            name?: string;
        }

        interface SharpTSSvgAttributes extends SharpTSDOMAttributes {
            xmlns?: string;
            viewBox?: string;
            width?: number | string;
            height?: number | string;
            x?: number | string;
            y?: number | string;
            x1?: number | string;
            y1?: number | string;
            x2?: number | string;
            y2?: number | string;
            cx?: number | string;
            cy?: number | string;
            r?: number | string;
            rx?: number | string;
            ry?: number | string;
            d?: string;
            points?: string;
            fill?: string;
            fillOpacity?: number | string;
            fillRule?: string;
            stroke?: string;
            strokeWidth?: number | string;
            strokeLinecap?: string;
            strokeLinejoin?: string;
            strokeDasharray?: number | string;
            strokeDashoffset?: number | string;
            strokeOpacity?: number | string;
            opacity?: number | string;
            transform?: string;
            href?: string;
            offset?: number | string;
            stopColor?: string;
            stopOpacity?: number | string;
            gradientUnits?: string;
            patternUnits?: string;
            preserveAspectRatio?: string;
            textAnchor?: string;
            dominantBaseline?: string;
            fontSize?: number | string;
            fontFamily?: string;
            clipPath?: string;
            mask?: string;
            filter?: string;
        }

        interface IntrinsicElements {
            // Document / metadata
            html: SharpTSHtmlAttributes;
            head: SharpTSDOMAttributes;
            body: SharpTSDOMAttributes;
            title: SharpTSDOMAttributes;
            base: SharpTSBaseAttributes;
            link: SharpTSLinkAttributes;
            meta: SharpTSMetaAttributes;
            style: SharpTSStyleAttributes;
            script: SharpTSScriptAttributes;
            noscript: SharpTSDOMAttributes;
            template: SharpTSDOMAttributes;
            slot: SharpTSSlotAttributes;

            // Sections
            address: SharpTSDOMAttributes;
            article: SharpTSDOMAttributes;
            aside: SharpTSDOMAttributes;
            footer: SharpTSDOMAttributes;
            header: SharpTSDOMAttributes;
            h1: SharpTSDOMAttributes;
            h2: SharpTSDOMAttributes;
            h3: SharpTSDOMAttributes;
            h4: SharpTSDOMAttributes;
            h5: SharpTSDOMAttributes;
            h6: SharpTSDOMAttributes;
            hgroup: SharpTSDOMAttributes;
            main: SharpTSDOMAttributes;
            nav: SharpTSDOMAttributes;
            section: SharpTSDOMAttributes;
            search: SharpTSDOMAttributes;

            // Grouping content
            blockquote: SharpTSQuoteAttributes;
            dd: SharpTSDOMAttributes;
            div: SharpTSDOMAttributes;
            dl: SharpTSDOMAttributes;
            dt: SharpTSDOMAttributes;
            figcaption: SharpTSDOMAttributes;
            figure: SharpTSDOMAttributes;
            hr: SharpTSDOMAttributes;
            li: SharpTSLiAttributes;
            menu: SharpTSDOMAttributes;
            ol: SharpTSOlAttributes;
            p: SharpTSDOMAttributes;
            pre: SharpTSDOMAttributes;
            ul: SharpTSDOMAttributes;

            // Text-level semantics
            a: SharpTSAnchorAttributes;
            abbr: SharpTSDOMAttributes;
            b: SharpTSDOMAttributes;
            bdi: SharpTSDOMAttributes;
            bdo: SharpTSDOMAttributes;
            br: SharpTSDOMAttributes;
            cite: SharpTSDOMAttributes;
            code: SharpTSDOMAttributes;
            data: SharpTSDataAttributes;
            dfn: SharpTSDOMAttributes;
            em: SharpTSDOMAttributes;
            i: SharpTSDOMAttributes;
            kbd: SharpTSDOMAttributes;
            mark: SharpTSDOMAttributes;
            q: SharpTSQuoteAttributes;
            rp: SharpTSDOMAttributes;
            rt: SharpTSDOMAttributes;
            ruby: SharpTSDOMAttributes;
            s: SharpTSDOMAttributes;
            samp: SharpTSDOMAttributes;
            small: SharpTSDOMAttributes;
            span: SharpTSDOMAttributes;
            strong: SharpTSDOMAttributes;
            sub: SharpTSDOMAttributes;
            sup: SharpTSDOMAttributes;
            time: SharpTSTimeAttributes;
            u: SharpTSDOMAttributes;
            var: SharpTSDOMAttributes;
            wbr: SharpTSDOMAttributes;

            // Edits
            del: SharpTSModAttributes;
            ins: SharpTSModAttributes;

            // Embedded content
            area: SharpTSAreaAttributes;
            audio: SharpTSMediaAttributes;
            canvas: SharpTSCanvasAttributes;
            embed: SharpTSEmbedAttributes;
            iframe: SharpTSIframeAttributes;
            img: SharpTSImageAttributes;
            map: SharpTSMapAttributes;
            object: SharpTSObjectAttributes;
            param: SharpTSParamAttributes;
            picture: SharpTSDOMAttributes;
            source: SharpTSSourceAttributes;
            track: SharpTSTrackAttributes;
            video: SharpTSVideoAttributes;

            // Tabular data
            caption: SharpTSDOMAttributes;
            col: SharpTSColAttributes;
            colgroup: SharpTSColAttributes;
            table: SharpTSDOMAttributes;
            tbody: SharpTSDOMAttributes;
            td: SharpTSTableCellAttributes;
            tfoot: SharpTSDOMAttributes;
            th: SharpTSTableCellAttributes;
            thead: SharpTSDOMAttributes;
            tr: SharpTSDOMAttributes;

            // Forms
            button: SharpTSButtonAttributes;
            datalist: SharpTSDOMAttributes;
            fieldset: SharpTSFieldsetAttributes;
            form: SharpTSFormAttributes;
            input: SharpTSInputAttributes;
            label: SharpTSLabelAttributes;
            legend: SharpTSDOMAttributes;
            meter: SharpTSMeterAttributes;
            optgroup: SharpTSOptgroupAttributes;
            option: SharpTSOptionAttributes;
            output: SharpTSOutputAttributes;
            progress: SharpTSProgressAttributes;
            select: SharpTSSelectAttributes;
            textarea: SharpTSTextareaAttributes;

            // Interactive elements
            details: SharpTSDetailsAttributes;
            dialog: SharpTSDialogAttributes;
            summary: SharpTSDOMAttributes;

            // SVG
            svg: SharpTSSvgAttributes;
            circle: SharpTSSvgAttributes;
            clipPath: SharpTSSvgAttributes;
            defs: SharpTSSvgAttributes;
            ellipse: SharpTSSvgAttributes;
            filter: SharpTSSvgAttributes;
            foreignObject: SharpTSSvgAttributes;
            g: SharpTSSvgAttributes;
            line: SharpTSSvgAttributes;
            linearGradient: SharpTSSvgAttributes;
            marker: SharpTSSvgAttributes;
            mask: SharpTSSvgAttributes;
            path: SharpTSSvgAttributes;
            pattern: SharpTSSvgAttributes;
            polygon: SharpTSSvgAttributes;
            polyline: SharpTSSvgAttributes;
            radialGradient: SharpTSSvgAttributes;
            rect: SharpTSSvgAttributes;
            stop: SharpTSSvgAttributes;
            symbol: SharpTSSvgAttributes;
            text: SharpTSSvgAttributes;
            tspan: SharpTSSvgAttributes;
            use: SharpTSSvgAttributes;
        }
    }
}
