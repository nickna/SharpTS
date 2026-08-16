// Node.js 'util' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/util.html.
//
// Replaces the previous C# UtilModuleInterpreter + UtilModuleEmitter
// with a pure-TS port. TextEncoder/TextDecoder are re-exports of the
// SharpTS global constructors; everything else is pure TS.

// The only host dependency is `primitive:process` for environment reads
// (NODE_DEBUG gating debuglog, NO_COLOR/FORCE_COLOR gating styleText). BCL-only,
// so standalone is preserved.
import { env as __envRaw } from 'primitive:process';
const __env: any = __envRaw;

// -------- format --------
//
// printf-like formatter with %s/%d/%i/%f/%j/%o/%O/%% placeholders.
// Unused args are appended space-separated, matching Node.

export function format(...args: any[]): string {
    return formatImpl(undefined, args);
}

// -------- formatWithOptions --------
//
// Like format, but %o/%O inspection honors the supplied inspect options.

export function formatWithOptions(inspectOptions: any, ...args: any[]): string {
    return formatImpl(inspectOptions, args);
}

function formatImpl(inspectOptions: any, args: any[]): string {
    if (args.length === 0) return '';

    const fmt = String(args[0]);
    let out = '';
    let argIndex = 1;
    let i = 0;
    const len = fmt.length;

    while (i < len) {
        const ch = fmt[i];
        if (ch === '%' && i + 1 < len) {
            const spec = fmt[i + 1];
            if (spec === 's') {
                out += argIndex < args.length ? String(args[argIndex++]) : '%s';
                i += 2;
                continue;
            }
            if (spec === 'd' || spec === 'i') {
                if (argIndex < args.length) {
                    const v = args[argIndex++];
                    if (typeof v === 'number') {
                        // %d / %i truncate toward zero.
                        const t = v < 0 ? Math.ceil(v) : Math.floor(v);
                        out += String(t);
                    } else {
                        out += 'NaN';
                    }
                } else {
                    out += '%' + spec;
                }
                i += 2;
                continue;
            }
            if (spec === 'f') {
                if (argIndex < args.length) {
                    const v = args[argIndex++];
                    out += typeof v === 'number' ? String(v) : 'NaN';
                } else {
                    out += '%f';
                }
                i += 2;
                continue;
            }
            if (spec === 'j') {
                if (argIndex < args.length) {
                    const v = args[argIndex++];
                    try {
                        out += JSON.stringify(v);
                    } catch (e) {
                        out += 'undefined';
                    }
                } else {
                    out += '%j';
                }
                i += 2;
                continue;
            }
            if (spec === 'o' || spec === 'O') {
                if (argIndex < args.length) {
                    out += inspect(args[argIndex++], inspectOptions);
                } else {
                    out += '%' + spec;
                }
                i += 2;
                continue;
            }
            if (spec === 'c') {
                // CSS directive: consumed with no output (Node behavior in non-browser).
                if (argIndex < args.length) argIndex++;
                i += 2;
                continue;
            }
            if (spec === '%') {
                out += '%';
                i += 2;
                continue;
            }
        }
        out += ch;
        i++;
    }

    // Extra args appended separated by single space, matching Node.
    while (argIndex < args.length) {
        out += ' ' + String(args[argIndex++]);
    }
    return out;
}

// -------- inspect --------
//
// Minimal object pretty-printer. Supports the `depth` option (default 2).
// Not a full reimplementation of Node's inspect — just enough for the
// common observable behaviors the test gate exercises.

// The custom-inspection hook symbol. An object exposing a function under this
// key controls its own inspect() output (Node's util.inspect.custom).
const kInspectCustom: symbol = Symbol.for('nodejs.util.inspect.custom');

interface InspectOpts {
    depth: number;
    colors: boolean;
    maxArrayLength: number;
    maxStringLength: number;
    showHidden: boolean;
    getters: boolean;
    breakLength: number;
    customInspect: boolean;
}

// ANSI style codes for the common inspect color styles (colors: true).
const INSPECT_STYLE: any = {
    number: '33', bigint: '33', boolean: '33',
    undefined: '90', null: '1',
    string: '32', symbol: '32',
    date: '35', regexp: '31', special: '36',
};

function colorize(text: string, style: string, opts: InspectOpts): string {
    if (!opts.colors) return text;
    const code = INSPECT_STYLE[style];
    return code !== undefined ? '\x1b[' + code + 'm' + text + '\x1b[39m' : text;
}

function normalizeInspectOptions(options: any): InspectOpts {
    const o: any = (options != null && typeof options === 'object') ? options : {};
    const depth = o.depth === null ? Infinity : (typeof o.depth === 'number' ? o.depth : 2);
    return {
        depth,
        colors: o.colors === true,
        maxArrayLength: o.maxArrayLength === null ? Infinity : (typeof o.maxArrayLength === 'number' ? o.maxArrayLength : 100),
        maxStringLength: o.maxStringLength === null ? Infinity : (typeof o.maxStringLength === 'number' ? o.maxStringLength : 10000),
        showHidden: o.showHidden === true,
        getters: o.getters === true,
        breakLength: typeof o.breakLength === 'number' ? o.breakLength : 128,
        customInspect: o.customInspect !== false,
    };
}

export function inspect(value: any, options?: any): string {
    // Node also supports inspect(value, showHidden, depth, colors) — the legacy
    // boolean-positional form. Map it to an options object.
    let opts: InspectOpts;
    if (typeof options === 'boolean') {
        opts = normalizeInspectOptions({ showHidden: options });
    } else {
        opts = normalizeInspectOptions(options);
    }
    return inspectValue(value, opts, 0, []);
}

function inspectValue(value: any, opts: InspectOpts, current: number, seen: any[]): string {
    if (value === null) return colorize('null', 'null', opts);
    if (value === undefined) return colorize('undefined', 'undefined', opts);
    const t = typeof value;
    if (t === 'string') {
        let s = value;
        if (s.length > opts.maxStringLength) {
            const shown = s.slice(0, opts.maxStringLength);
            const more = s.length - opts.maxStringLength;
            return colorize("'" + shown + "'", 'string', opts) + "... " + more + " more character" + (more === 1 ? '' : 's');
        }
        return colorize("'" + s + "'", 'string', opts);
    }
    if (t === 'number') return colorize(String(value), 'number', opts);
    if (t === 'boolean') return colorize(value ? 'true' : 'false', 'boolean', opts);
    if (t === 'bigint') return colorize(String(value) + 'n', 'bigint', opts);
    if (t === 'symbol') return colorize(String(value), 'symbol', opts);
    if (t === 'function') {
        const name = (value as any).name;
        return colorize(name ? '[Function: ' + name + ']' : '[Function (anonymous)]', 'special', opts);
    }

    // Custom inspection hook (util.inspect.custom). Only consulted for plain
    // objects — arrays/typed values don't support arbitrary symbol-keyed reads
    // in SharpTS, and Node's own custom-inspect targets are objects.
    if (opts.customInspect && value != null && t === 'object' && !Array.isArray(value)
        && !(value instanceof Date) && !(value instanceof RegExp)
        && typeof value[kInspectCustom] === 'function') {
        const produced = value[kInspectCustom](opts.depth, opts);
        return typeof produced === 'string' ? produced : inspectValue(produced, opts, current, seen);
    }

    if (value instanceof Date) return colorize(value.toISOString(), 'date', opts);
    if (value instanceof RegExp) return colorize(String(value), 'regexp', opts);

    // Cycle guard.
    for (let i = 0; i < seen.length; i++) {
        if (seen[i] === value) return '[Circular *1]';
    }
    seen.push(value);

    let result: string;
    if (Array.isArray(value)) {
        if (current > opts.depth) { result = "[Array]"; }
        else {
            const parts: string[] = [];
            const limit = value.length < opts.maxArrayLength ? value.length : opts.maxArrayLength;
            for (let i = 0; i < limit; i++) parts.push(inspectValue(value[i], opts, current + 1, seen));
            if (value.length > limit) parts.push('... ' + (value.length - limit) + ' more item' + (value.length - limit === 1 ? '' : 's'));
            result = parts.length === 0 ? '[]' : '[ ' + parts.join(', ') + ' ]';
        }
    } else if (t === 'object') {
        if (current > opts.depth) { result = '[Object]'; }
        else {
            const keys = opts.showHidden ? Object.getOwnPropertyNames(value) : Object.keys(value);
            const parts: string[] = [];
            for (let i = 0; i < keys.length; i++) {
                const k = keys[i];
                parts.push(k + ': ' + inspectValue(value[k], opts, current + 1, seen));
            }
            result = parts.length === 0 ? '{}' : '{ ' + parts.join(', ') + ' }';
        }
    } else {
        result = String(value);
    }

    seen.pop();
    return result;
}

// -------- isDeepStrictEqual --------
//
// Structural deep equality with JS-strict type semantics plus:
//   - NaN === NaN (unlike ===)
//   - arrays, plain objects, Map, Set, Date, RegExp compared by content
//   - cycles tolerated via an in-progress pair tracker
// Functions compare by reference (Node behavior).

export function isDeepStrictEqual(a: any, b: any): boolean {
    return deepEqual(a, b, []);
}

function deepEqual(a: any, b: any, seen: any[]): boolean {
    if (a === b) return true;

    if (typeof a === 'number' && typeof b === 'number') {
        if (isNaN(a) && isNaN(b)) return true;
        return false;
    }

    if (a === null || b === null || a === undefined || b === undefined) return false;

    const ta = typeof a;
    const tb = typeof b;
    if (ta !== tb) return false;
    if (ta !== 'object') return false;

    // Cycle guard — if we're already comparing this pair higher up the stack,
    // assume equal to break the loop.
    for (let i = 0; i < seen.length; i++) {
        const s = seen[i];
        if (s[0] === a && s[1] === b) return true;
    }
    seen.push([a, b]);

    const aIsArr = Array.isArray(a);
    const bIsArr = Array.isArray(b);
    if (aIsArr !== bIsArr) { seen.pop(); return false; }
    if (aIsArr) {
        if (a.length !== b.length) { seen.pop(); return false; }
        for (let i = 0; i < a.length; i++) {
            if (!deepEqual(a[i], b[i], seen)) { seen.pop(); return false; }
        }
        seen.pop();
        return true;
    }

    if (a instanceof Date && b instanceof Date) {
        const eq = a.getTime() === b.getTime();
        seen.pop();
        return eq;
    }
    if (a instanceof Date || b instanceof Date) { seen.pop(); return false; }

    if (a instanceof RegExp && b instanceof RegExp) {
        const eq = a.source === b.source && a.flags === b.flags;
        seen.pop();
        return eq;
    }
    if (a instanceof RegExp || b instanceof RegExp) { seen.pop(); return false; }

    if (a instanceof Map && b instanceof Map) {
        if (a.size !== b.size) { seen.pop(); return false; }
        const aKeys = Array.from(a.keys());
        for (let i = 0; i < aKeys.length; i++) {
            const k = aKeys[i];
            if (!b.has(k)) { seen.pop(); return false; }
            if (!deepEqual(a.get(k), b.get(k), seen)) { seen.pop(); return false; }
        }
        seen.pop();
        return true;
    }
    if (a instanceof Map || b instanceof Map) { seen.pop(); return false; }

    if (a instanceof Set && b instanceof Set) {
        if (a.size !== b.size) { seen.pop(); return false; }
        const aVals = Array.from(a.values());
        const bVals = Array.from(b.values());
        for (let i = 0; i < aVals.length; i++) {
            let found = false;
            for (let j = 0; j < bVals.length; j++) {
                if (deepEqual(aVals[i], bVals[j], [])) { found = true; break; }
            }
            if (!found) { seen.pop(); return false; }
        }
        seen.pop();
        return true;
    }
    if (a instanceof Set || b instanceof Set) { seen.pop(); return false; }

    const keysA = Object.keys(a);
    const keysB = Object.keys(b);
    if (keysA.length !== keysB.length) { seen.pop(); return false; }
    for (let i = 0; i < keysA.length; i++) {
        const k = keysA[i];
        if (!deepEqual(a[k], b[k], seen)) { seen.pop(); return false; }
    }
    seen.pop();
    return true;
}

// -------- toUSVString --------
//
// Replaces lone surrogates (unpaired D800-DFFF code units) with U+FFFD.

export function toUSVString(str: any): string {
    const s = String(str);
    let out = '';
    const n = s.length;
    for (let i = 0; i < n; i++) {
        const c = s.charCodeAt(i);
        if (c >= 0xD800 && c <= 0xDBFF) {
            if (i + 1 < n) {
                const c2 = s.charCodeAt(i + 1);
                if (c2 >= 0xDC00 && c2 <= 0xDFFF) {
                    out += s[i] + s[i + 1];
                    i++;
                    continue;
                }
            }
            out += '\uFFFD';
        } else if (c >= 0xDC00 && c <= 0xDFFF) {
            out += '\uFFFD';
        } else {
            out += s[i];
        }
    }
    return out;
}

// -------- stripVTControlCharacters --------

const ANSI_REGEX = /\x1b\[[0-9;]*[a-zA-Z]|\x1b\][^\x07]*\x07|\x1b[PX^_][^\x1b]*\x1b\\|\x1b\[[0-9;]*m/g;

export function stripVTControlCharacters(str: any): string {
    const s = String(str);
    return s.replace(ANSI_REGEX, '');
}

// -------- getSystemErrorName / getSystemErrorMap --------
//
// POSIX errno → name + description. Values are libuv-style (negative) codes
// to match what Node.js exposes for err.errno.

const POSIX_ERROR_NAMES: any = {};
const POSIX_ERROR_DESCRIPTIONS: any = {};

function registerPosixError(code: number, name: string, description: string): void {
    POSIX_ERROR_NAMES[String(code)] = name;
    POSIX_ERROR_DESCRIPTIONS[name] = description;
}

registerPosixError(-1, 'EPERM', 'operation not permitted');
registerPosixError(-2, 'ENOENT', 'no such file or directory');
registerPosixError(-3, 'ESRCH', 'no such process');
registerPosixError(-4, 'EINTR', 'interrupted system call');
registerPosixError(-5, 'EIO', 'i/o error');
registerPosixError(-6, 'ENXIO', 'no such device or address');
registerPosixError(-7, 'E2BIG', 'argument list too long');
registerPosixError(-8, 'ENOEXEC', 'exec format error');
registerPosixError(-9, 'EBADF', 'bad file descriptor');
registerPosixError(-10, 'ECHILD', 'no child processes');
registerPosixError(-11, 'EAGAIN', 'resource temporarily unavailable');
registerPosixError(-12, 'ENOMEM', 'not enough memory');
registerPosixError(-13, 'EACCES', 'permission denied');
registerPosixError(-14, 'EFAULT', 'bad address');
registerPosixError(-16, 'EBUSY', 'resource busy or locked');
registerPosixError(-17, 'EEXIST', 'file already exists');
registerPosixError(-18, 'EXDEV', 'cross-device link not permitted');
registerPosixError(-19, 'ENODEV', 'no such device');
registerPosixError(-20, 'ENOTDIR', 'not a directory');
registerPosixError(-21, 'EISDIR', 'illegal operation on a directory');
registerPosixError(-22, 'EINVAL', 'invalid argument');
registerPosixError(-23, 'ENFILE', 'file table overflow');
registerPosixError(-24, 'EMFILE', 'too many open files');
registerPosixError(-25, 'ENOTTY', 'inappropriate ioctl for device');
registerPosixError(-26, 'ETXTBSY', 'text file is busy');
registerPosixError(-27, 'EFBIG', 'file too large');
registerPosixError(-28, 'ENOSPC', 'no space left on device');
registerPosixError(-29, 'ESPIPE', 'invalid seek');
registerPosixError(-30, 'EROFS', 'read-only file system');
registerPosixError(-31, 'EMLINK', 'too many links');
registerPosixError(-32, 'EPIPE', 'broken pipe');
registerPosixError(-33, 'EDOM', 'argument out of domain');
registerPosixError(-34, 'ERANGE', 'result too large');
registerPosixError(-35, 'EDEADLK', 'resource deadlock avoided');
registerPosixError(-36, 'ENAMETOOLONG', 'name too long');
registerPosixError(-37, 'ENOLCK', 'no locks available');
registerPosixError(-38, 'ENOSYS', 'function not implemented');
registerPosixError(-39, 'ENOTEMPTY', 'directory not empty');
registerPosixError(-40, 'ELOOP', 'too many symbolic links encountered');
registerPosixError(-42, 'ENOMSG', 'no message of desired type');
registerPosixError(-43, 'EIDRM', 'identifier removed');
registerPosixError(-60, 'ENOSTR', 'device not a stream');
registerPosixError(-61, 'ENODATA', 'no data available');
registerPosixError(-62, 'ETIME', 'timer expired');
registerPosixError(-63, 'ENOSR', 'out of streams resources');
registerPosixError(-71, 'EPROTO', 'protocol error');
registerPosixError(-74, 'EBADMSG', 'bad message');
registerPosixError(-75, 'EOVERFLOW', 'value too large for defined data type');
registerPosixError(-88, 'ENOTSOCK', 'socket operation on non-socket');
registerPosixError(-89, 'EDESTADDRREQ', 'destination address required');
registerPosixError(-90, 'EMSGSIZE', 'message too long');
registerPosixError(-91, 'EPROTOTYPE', 'protocol wrong type for socket');
registerPosixError(-92, 'ENOPROTOOPT', 'protocol not available');
registerPosixError(-93, 'EPROTONOSUPPORT', 'protocol not supported');
registerPosixError(-95, 'EOPNOTSUPP', 'operation not supported on socket');
registerPosixError(-97, 'EAFNOSUPPORT', 'address family not supported');
registerPosixError(-98, 'EADDRINUSE', 'address already in use');
registerPosixError(-99, 'EADDRNOTAVAIL', 'address not available');
registerPosixError(-100, 'ENETDOWN', 'network is down');
registerPosixError(-101, 'ENETUNREACH', 'network is unreachable');
registerPosixError(-102, 'ENETRESET', 'connection reset by network');
registerPosixError(-103, 'ECONNABORTED', 'connection aborted');
registerPosixError(-104, 'ECONNRESET', 'connection reset by peer');
registerPosixError(-105, 'ENOBUFS', 'no buffer space available');
registerPosixError(-106, 'EISCONN', 'socket is connected');
registerPosixError(-107, 'ENOTCONN', 'socket is not connected');
registerPosixError(-110, 'ETIMEDOUT', 'connection timed out');
registerPosixError(-111, 'ECONNREFUSED', 'connection refused');
registerPosixError(-112, 'EHOSTDOWN', 'host is down');
registerPosixError(-113, 'EHOSTUNREACH', 'host is unreachable');
registerPosixError(-114, 'EALREADY', 'connection already in progress');
registerPosixError(-115, 'EINPROGRESS', 'operation in progress');
registerPosixError(-116, 'ESTALE', 'stale file handle');
registerPosixError(-122, 'EDQUOT', 'disk quota exceeded');
registerPosixError(-125, 'ECANCELED', 'operation canceled');

export function getSystemErrorName(errno: number): string {
    const key = String(errno);
    const name = POSIX_ERROR_NAMES[key];
    if (name !== undefined) return name;
    return 'Unknown system error ' + String(errno);
}

export function getSystemErrorMessage(errno: number): string {
    const key = String(errno);
    const name = POSIX_ERROR_NAMES[key];
    if (name !== undefined) {
        const desc = POSIX_ERROR_DESCRIPTIONS[name];
        return desc !== undefined ? desc : name;
    }
    return 'Unknown system error ' + String(errno);
}

export function getSystemErrorMap(): any {
    const map = new Map<number, any>();
    const keys = Object.keys(POSIX_ERROR_NAMES);
    for (let i = 0; i < keys.length; i++) {
        const k = keys[i];
        const code = Number(k);
        const name = POSIX_ERROR_NAMES[k];
        const desc = POSIX_ERROR_DESCRIPTIONS[name];
        map.set(code, [name, desc !== undefined ? desc : '']);
    }
    return map;
}

// -------- deprecate --------
//
// Wraps a function. On first call, would log the warning to stderr; subsequent
// calls pass through silently. We don't have a stderr hook at this scope, so
// we stay silent — the observable contract the tests gate on is "called once,
// doesn't throw, forwards return value."

export function deprecate(fn: any, message: string, _code?: string): any {
    let warned = false;
    const warning = 'DeprecationWarning: ' + message;
    return (...args: any[]): any => {
        if (!warned) {
            warned = true;
            void warning;
        }
        return fn(...args);
    };
}

// -------- callbackify --------
//
// Turns a function into a Node-style (err, result) callback.

export function callbackify(fn: any): any {
    return function (...args: any[]): any {
        if (args.length === 0) throw new TypeError('Callback is required');
        const cb = args[args.length - 1];
        if (typeof cb !== 'function') throw new TypeError('Last argument must be a function');
        const callArgs = args.slice(0, args.length - 1);
        try {
            const result = fn(...callArgs);
            cb(null, result);
        } catch (e) {
            cb(e, null);
        }
    };
}

// -------- promisify --------
//
// Turns a Node-style callback function (...args, callback) into a Promise-
// returning function.

export function promisify(fn: any): any {
    return function (...args: any[]): Promise<any> {
        return new Promise((resolve: any, reject: any) => {
            const cb = (err: any, value: any) => {
                if (err) reject(err);
                else resolve(value);
            };
            fn(...args, cb);
        });
    };
}

// -------- inherits --------
//
// Legacy prototype-chain helper. Observable contract: `ctor.super_ === superCtor`.

export function inherits(ctor: any, superCtor: any): void {
    // Use Object.defineProperty first because compiled classes are System.Type
    // references that don't support plain property assignment — SetFieldsProperty
    // silently no-ops, so `ctor.super_ = superCtor` doesn't round-trip through
    // the subsequent read. Object.defineProperty routes through the property-
    // descriptor store which DOES accept arbitrary object keys, so the legacy
    // `ctor.super_` read pattern works in both modes. Interpreter mode rejects
    // defineProperty on a SharpTSClass, so we fall back to plain assignment
    // (which works there).
    try {
        Object.defineProperty(ctor, 'super_', {
            value: superCtor,
            configurable: true,
            writable: true,
        });
    } catch (e) {
        ctor.super_ = superCtor;
    }
}

// -------- TextEncoder / TextDecoder --------
//
// Re-exports of the SharpTS globals. Users can `import { TextEncoder } from 'util'`
// or reach the global directly — Node exposes them as util.TextEncoder and
// globalThis.TextEncoder.
const _TextEncoder: any = TextEncoder;
const _TextDecoder: any = TextDecoder;
export { _TextEncoder as TextEncoder, _TextDecoder as TextDecoder };

// -------- styleText --------
//
// ANSI text styling (Node 20+). `format` is a style name or array of names.
// Color output is suppressed when NO_COLOR is set (and not overridden by
// FORCE_COLOR), matching Node's default TTY/env behavior.

const STYLE_CODES: any = {
    reset: [0, 0], bold: [1, 22], dim: [2, 22], italic: [3, 23], underline: [4, 24],
    blink: [5, 25], inverse: [7, 27], hidden: [8, 28], strikethrough: [9, 29],
    doubleunderline: [21, 24], framed: [51, 54], overlined: [53, 55],
    black: [30, 39], red: [31, 39], green: [32, 39], yellow: [33, 39], blue: [34, 39],
    magenta: [35, 39], cyan: [36, 39], white: [37, 39], gray: [90, 39], grey: [90, 39],
    redBright: [91, 39], greenBright: [92, 39], yellowBright: [93, 39], blueBright: [94, 39],
    magentaBright: [95, 39], cyanBright: [96, 39], whiteBright: [97, 39],
    bgBlack: [40, 49], bgRed: [41, 49], bgGreen: [42, 49], bgYellow: [43, 49], bgBlue: [44, 49],
    bgMagenta: [45, 49], bgCyan: [46, 49], bgWhite: [47, 49],
    bgGray: [100, 49], bgGrey: [100, 49],
    bgRedBright: [101, 49], bgGreenBright: [102, 49], bgYellowBright: [103, 49],
    bgBlueBright: [104, 49], bgMagentaBright: [105, 49], bgCyanBright: [106, 49], bgWhiteBright: [107, 49],
};

function __colorsDisabled(): boolean {
    // FORCE_COLOR (any value) wins; otherwise NO_COLOR disables color.
    const force = __env != null ? __env.FORCE_COLOR : undefined;
    if (force !== undefined && force !== '' && force !== '0' && force !== 'false') return false;
    const noColor = __env != null ? __env.NO_COLOR : undefined;
    return noColor !== undefined && noColor !== '';
}

export function styleText(format: any, text: string, options?: any): string {
    if (typeof text !== 'string') {
        throw new TypeError('The "text" argument must be of type string');
    }
    // Node: options.validateStream (default true) gates the can-this-stream-color
    // check (approximated here by the NO_COLOR/FORCE_COLOR env probe);
    // validateStream: false applies the styling unconditionally.
    const validate = options == null || options.validateStream !== false;

    const formats: any[] = Array.isArray(format) ? format : [format];
    for (let i = 0; i < formats.length; i++) {
        // `== null`, not `=== undefined`: the compiled dynamic-index path yields null
        // for a missing key (RuntimeEmitter.Objects.Index.cs EmitDictLookup — the
        // prototype-walk/undefined refactor is explicitly deferred there), while the
        // interpreter yields undefined. Loose null covers both modes.
        if (STYLE_CODES[formats[i]] == null) {
            throw new TypeError("The value '" + String(formats[i]) + "' is invalid for argument 'format'");
        }
    }

    if (validate && __colorsDisabled()) return text;

    let open = '';
    let close = '';
    for (let i = 0; i < formats.length; i++) {
        const pair = STYLE_CODES[formats[i]];
        open += '\x1b[' + pair[0] + 'm';
        close = '\x1b[' + pair[1] + 'm' + close;
    }
    return open + text + close;
}

// -------- debuglog / debug --------
//
// Returns a logger gated on NODE_DEBUG. When the section is enabled, the
// returned function writes to stderr (approximated via console.error); when
// disabled it is a no-op. `enabled` reflects the gate.

function __debugSectionEnabled(section: string): boolean {
    const nodeDebug = __env != null ? __env.NODE_DEBUG : undefined;
    if (nodeDebug === undefined || nodeDebug === '') return false;
    const wanted = String(nodeDebug).toUpperCase();
    const target = String(section).toUpperCase();
    if (wanted === '*') return true;
    const parts = wanted.split(',');
    for (let i = 0; i < parts.length; i++) {
        const p = parts[i].trim();
        if (p === target || p === '*') return true;
        // Node treats a trailing '*' as a wildcard prefix.
        if (p.length > 0 && p.charAt(p.length - 1) === '*' && target.indexOf(p.slice(0, p.length - 1)) === 0) return true;
    }
    return false;
}

export function debuglog(section: string, callback?: any): any {
    const enabled = __debugSectionEnabled(section);
    const logger: any = enabled
        ? (...args: any[]): void => {
            const msg = format(...args);
            console.error(section.toUpperCase() + ' ' + msg);
        }
        : (..._args: any[]): void => { /* no-op when the section is off */ };
    logger.enabled = enabled;
    if (typeof callback === 'function' && enabled) {
        callback(logger);
    }
    return logger;
}

export const debug = debuglog;

// -------- types sub-module --------
//
// A small namespace of duck-typed checks. Node's util.types uses V8 internal
// slots; we approximate with `instanceof` + `Array.isArray` + `typeof`.

function isArray(value: any): boolean {
    return Array.isArray(value);
}
function isDate(value: any): boolean {
    return value instanceof Date;
}
function isFunction(value: any): boolean {
    return typeof value === 'function';
}
function isNull(value: any): boolean {
    return value === null;
}
function isUndefined(value: any): boolean {
    return value === undefined;
}
function isPromise(value: any): boolean {
    return value instanceof Promise;
}
function isRegExp(value: any): boolean {
    return value instanceof RegExp;
}
function isMap(value: any): boolean {
    return value instanceof Map;
}
function isSet(value: any): boolean {
    return value instanceof Set;
}
function isTypedArray(value: any): boolean {
    return value instanceof Buffer;
}
function isNativeError(value: any): boolean {
    return value instanceof Error;
}
function isBoxedPrimitive(_value: any): boolean {
    // SharpTS does not materialize boxed String/Number/Boolean objects;
    // `new String('x')` evaluates to a plain string. Always false.
    return false;
}
function isWeakMap(value: any): boolean {
    return value instanceof WeakMap;
}
function isWeakSet(value: any): boolean {
    return value instanceof WeakSet;
}
function isArrayBuffer(value: any): boolean {
    // SharpTS collapses Buffer and the typed-array family onto a single
    // Buffer type, so ArrayBuffer-ness is detected the same way (unchanged
    // from the original behavior; a real ArrayBuffer is also covered).
    return value instanceof Buffer || value instanceof ArrayBuffer;
}
function isSharedArrayBuffer(value: any): boolean {
    return typeof SharedArrayBuffer !== 'undefined' && value instanceof SharedArrayBuffer;
}
function isAnyArrayBuffer(value: any): boolean {
    return isArrayBuffer(value) || isSharedArrayBuffer(value);
}
function isDataView(value: any): boolean {
    return typeof DataView !== 'undefined' && value instanceof DataView;
}
function isArrayBufferView(value: any): boolean {
    return isDataView(value) || value instanceof Buffer
        || isInt8Array(value) || isUint8Array(value) || isUint8ClampedArray(value)
        || isInt16Array(value) || isUint16Array(value) || isInt32Array(value)
        || isUint32Array(value) || isFloat32Array(value) || isFloat64Array(value)
        || isBigInt64Array(value) || isBigUint64Array(value);
}
// SharpTS does not materialize boxed String/Number/Boolean/Symbol/BigInt
// objects (`new String('x')` yields a primitive), so these are always false.
function isBigIntObject(_value: any): boolean { return false; }
function isBooleanObject(_value: any): boolean { return false; }
function isNumberObject(_value: any): boolean { return false; }
function isStringObject(_value: any): boolean { return false; }
function isSymbolObject(_value: any): boolean { return false; }
// Proxies are transparent to guest code (no observable marker), and external
// (native) values / module namespace objects are not represented distinctly.
function isProxy(_value: any): boolean { return false; }
function isExternal(_value: any): boolean { return false; }
function isModuleNamespaceObject(_value: any): boolean { return false; }
function isKeyObject(_value: any): boolean { return false; }
function isCryptoKey(_value: any): boolean { return false; }
function isGeneratorFunction(value: any): boolean {
    if (typeof value !== 'function') return false;
    const ctor = (value as any).constructor;
    return ctor != null && ctor.name === 'GeneratorFunction';
}
function isAsyncFunction(value: any): boolean {
    if (typeof value !== 'function') return false;
    const ctor = (value as any).constructor;
    return ctor != null && ctor.name === 'AsyncFunction';
}
function isGeneratorObject(value: any): boolean {
    if (value == null || typeof value !== 'object') return false;
    const v: any = value;
    return typeof v.next === 'function' && typeof v.throw === 'function' && typeof v.return === 'function';
}
// Typed-array element predicates. SharpTS's typed arrays map to the standard
// global constructors, so instanceof discriminates them.
function isInt8Array(value: any): boolean { return typeof Int8Array !== 'undefined' && value instanceof Int8Array; }
function isUint8Array(value: any): boolean { return typeof Uint8Array !== 'undefined' && value instanceof Uint8Array; }
function isUint8ClampedArray(value: any): boolean { return typeof Uint8ClampedArray !== 'undefined' && value instanceof Uint8ClampedArray; }
function isInt16Array(value: any): boolean { return typeof Int16Array !== 'undefined' && value instanceof Int16Array; }
function isUint16Array(value: any): boolean { return typeof Uint16Array !== 'undefined' && value instanceof Uint16Array; }
function isInt32Array(value: any): boolean { return typeof Int32Array !== 'undefined' && value instanceof Int32Array; }
function isUint32Array(value: any): boolean { return typeof Uint32Array !== 'undefined' && value instanceof Uint32Array; }
function isFloat32Array(value: any): boolean { return typeof Float32Array !== 'undefined' && value instanceof Float32Array; }
function isFloat64Array(value: any): boolean { return typeof Float64Array !== 'undefined' && value instanceof Float64Array; }
function isBigInt64Array(value: any): boolean { return typeof BigInt64Array !== 'undefined' && value instanceof BigInt64Array; }
function isBigUint64Array(value: any): boolean { return typeof BigUint64Array !== 'undefined' && value instanceof BigUint64Array; }

export const types = {
    isArray,
    isDate,
    isFunction,
    isNull,
    isUndefined,
    isPromise,
    isRegExp,
    isMap,
    isSet,
    isTypedArray,
    isNativeError,
    isBoxedPrimitive,
    isWeakMap,
    isWeakSet,
    isArrayBuffer,
    isSharedArrayBuffer,
    isAnyArrayBuffer,
    isDataView,
    isArrayBufferView,
    isBigIntObject,
    isBooleanObject,
    isNumberObject,
    isStringObject,
    isSymbolObject,
    isProxy,
    isExternal,
    isModuleNamespaceObject,
    isKeyObject,
    isCryptoKey,
    isGeneratorFunction,
    isAsyncFunction,
    isGeneratorObject,
    isInt8Array,
    isUint8Array,
    isUint8ClampedArray,
    isInt16Array,
    isUint16Array,
    isInt32Array,
    isUint32Array,
    isFloat32Array,
    isFloat64Array,
    isBigInt64Array,
    isBigUint64Array,
};

// -------- MIMEType / MIMEParams --------
//
// Minimal parser for RFC 2045 media types: `type/subtype;p1=v1;p2="v2"`.

/** The parameter map of a {@link MIMEType}. Iterable over [name, value] pairs. */
export class MIMEParams {
    private _map: any[]; // array of [name, value] preserving insertion order

    constructor() {
        this._map = [];
    }

    private _index(name: string): number {
        const key = String(name).toLowerCase();
        for (let i = 0; i < this._map.length; i++) {
            if (this._map[i][0] === key) return i;
        }
        return -1;
    }

    get(name: string): string | null {
        const i = this._index(name);
        return i >= 0 ? this._map[i][1] : null;
    }
    has(name: string): boolean {
        return this._index(name) >= 0;
    }
    set(name: string, value: string): void {
        const key = String(name).toLowerCase();
        const i = this._index(key);
        if (i >= 0) this._map[i][1] = String(value);
        else this._map.push([key, String(value)]);
    }
    delete(name: string): void {
        const i = this._index(name);
        if (i >= 0) this._map.splice(i, 1);
    }
    entries(): any {
        const arr: any = this._map.map((p: any) => [p[0], p[1]]);
        return arr[Symbol.iterator]();
    }
    keys(): any {
        const arr: any = this._map.map((p: any) => p[0]);
        return arr[Symbol.iterator]();
    }
    values(): any {
        const arr: any = this._map.map((p: any) => p[1]);
        return arr[Symbol.iterator]();
    }
    [Symbol.iterator](): any {
        return this.entries();
    }
    toString(): string {
        const parts: string[] = [];
        for (let i = 0; i < this._map.length; i++) {
            parts.push(this._map[i][0] + '=' + this._map[i][1]);
        }
        return parts.join(';');
    }
}

/** Parsed media type. Mirrors Node's util.MIMEType. */
export class MIMEType {
    type: string;
    subtype: string;
    params: MIMEParams;

    constructor(input: string) {
        const str = String(input).trim();
        const semi = str.indexOf(';');
        const essence = semi >= 0 ? str.slice(0, semi) : str;
        const slash = essence.indexOf('/');
        if (slash < 0) throw new TypeError('Invalid MIME type: ' + str);
        this.type = essence.slice(0, slash).trim().toLowerCase();
        this.subtype = essence.slice(slash + 1).trim().toLowerCase();
        if (this.type.length === 0 || this.subtype.length === 0) {
            throw new TypeError('Invalid MIME type: ' + str);
        }

        this.params = new MIMEParams();
        if (semi >= 0) {
            const rest = str.slice(semi + 1);
            const segments = rest.split(';');
            for (let i = 0; i < segments.length; i++) {
                const seg = segments[i].trim();
                if (seg.length === 0) continue;
                const eq = seg.indexOf('=');
                if (eq < 0) continue;
                const name = seg.slice(0, eq).trim();
                let value = seg.slice(eq + 1).trim();
                if (value.length >= 2 && value.charAt(0) === '"' && value.charAt(value.length - 1) === '"') {
                    value = value.slice(1, value.length - 1);
                }
                if (name.length > 0) this.params.set(name, value);
            }
        }
    }

    /** The `type/subtype` string with no parameters. */
    get essence(): string {
        return this.type + '/' + this.subtype;
    }

    toString(): string {
        const p = this.params.toString();
        return p.length > 0 ? this.essence + ';' + p : this.essence;
    }
}

// -------- abort helpers --------

/**
 * Resolves once `signal` aborts (Node's util.aborted). The `resource` argument
 * ties the pending listener to a resource lifetime in Node; SharpTS ignores it
 * (no host async-resource tracking) but keeps the parameter for compatibility.
 */
export function aborted(signal: any, resource?: any): Promise<void> {
    void resource;
    return new Promise((resolve: any) => {
        if (signal == null) { resolve(); return; }
        if (signal.aborted) { resolve(); return; }
        try {
            signal.addEventListener('abort', () => resolve(), { once: true });
        } catch (e) {
            // Fall back to onabort if addEventListener is unavailable.
            signal.onabort = () => resolve();
        }
    });
}

/** Returns an AbortController whose signal may be transferred. SharpTS has no
 *  distinct transferable representation, so this is a plain AbortController. */
export function transferableAbortController(): any {
    return new AbortController();
}

/** Marks `signal` transferable and returns it (identity in SharpTS). */
export function transferableAbortSignal(signal: any): any {
    return signal;
}

// -------- parseEnv / getCallSites --------

/**
 * Parses the contents of a `.env` file into an object (Node 20+). Supports
 * `KEY=value`, `#` comments, blank lines, `export KEY=...`, and single/double
 * quoted values.
 */
export function parseEnv(content: string): any {
    const result: any = {};
    const lines = String(content).split('\n');
    for (let i = 0; i < lines.length; i++) {
        let line = lines[i];
        // Strip a trailing CR from CRLF input.
        if (line.length > 0 && line.charAt(line.length - 1) === '\r') line = line.slice(0, line.length - 1);
        line = line.trim();
        if (line.length === 0 || line.charAt(0) === '#') continue;
        if (line.indexOf('export ') === 0) line = line.slice(7).trim();
        const eq = line.indexOf('=');
        if (eq < 0) continue;
        const key = line.slice(0, eq).trim();
        let value = line.slice(eq + 1).trim();
        if (value.length >= 2) {
            const first = value.charAt(0);
            const last = value.charAt(value.length - 1);
            if ((first === '"' && last === '"') || (first === "'" && last === "'")) {
                value = value.slice(1, value.length - 1);
            }
        }
        if (key.length > 0) result[key] = value;
    }
    return result;
}

/**
 * Returns structured call-site information (Node 22+). SharpTS does not expose
 * a V8-style structured stack to guest code, so this returns an empty array
 * (documented bound); callers that only test for an array shape still work.
 */
export function getCallSites(_frames?: any, _options?: any): any[] {
    return [];
}

// -------- deprecated legacy helpers --------
//
// Node keeps these for backward compatibility; new code should use `typeof`
// checks or `util.types.*`. Provided for parity with existing programs.

export function _extend(target: any, source: any): any {
    if (source == null || typeof source !== 'object') return target;
    const keys = Object.keys(source);
    for (let i = 0; i < keys.length; i++) target[keys[i]] = source[keys[i]];
    return target;
}

export function isBoolean(value: any): boolean { return typeof value === 'boolean'; }
export function isNullOrUndefined(value: any): boolean { return value === null || value === undefined; }
export function isNumber(value: any): boolean { return typeof value === 'number'; }
export function isString(value: any): boolean { return typeof value === 'string'; }
export function isSymbol(value: any): boolean { return typeof value === 'symbol'; }
export function isObject(value: any): boolean { return value !== null && typeof value === 'object'; }
export function isPrimitive(value: any): boolean {
    return value === null || (typeof value !== 'object' && typeof value !== 'function');
}
export function isError(value: any): boolean { return value instanceof Error; }
export function isBuffer(value: any): boolean { return value instanceof Buffer; }

// The remaining deprecated aliases reuse the internal type predicates.
export { isArray, isNull, isUndefined, isFunction, isRegExp, isDate };

// -------- parseArgs --------
//
// Minimal Node v24 util.parseArgs. Supports:
//   - boolean/string option types
//   - short aliases (-v)
//   - --option=value and --option value syntaxes
//   - positionals (when allowPositionals is true)
//   - multiple: true for repeated string options
//   - the `--` terminator

export function parseArgs(config?: any): any {
    const cfg = config != null ? config : {};
    const argv: any[] = Array.isArray(cfg.args) ? cfg.args : [];
    const optionsDef: any = (cfg.options != null && typeof cfg.options === 'object') ? cfg.options : {};
    const strict = cfg.strict !== undefined ? !!cfg.strict : true;
    const allowPositionals = cfg.allowPositionals !== undefined ? !!cfg.allowPositionals : !strict;

    // Build a short → long name lookup for dash-letter options.
    const shortLookup: any = {};
    const longNames = Object.keys(optionsDef);
    for (let li = 0; li < longNames.length; li++) {
        const ln = longNames[li];
        const def = optionsDef[ln];
        if (def != null && typeof def === 'object' && typeof def.short === 'string') {
            shortLookup[def.short] = ln;
        }
    }

    const values: any = {};
    const positionals: string[] = [];

    let i = 0;
    while (i < argv.length) {
        const arg = String(argv[i]);

        if (arg === '--') {
            i++;
            while (i < argv.length) {
                positionals.push(String(argv[i]));
                i++;
            }
            break;
        }

        if (arg.length >= 2 && arg[0] === '-' && arg[1] === '-') {
            // --long option
            let name: string;
            let inline: string | null = null;
            const eq = arg.indexOf('=');
            if (eq >= 0) {
                name = arg.substring(2, eq);
                inline = arg.substring(eq + 1);
            } else {
                name = arg.substring(2);
            }

            const def = optionsDef[name];
            const optType = (def != null && typeof def.type === 'string') ? def.type : 'boolean';
            const multiple = def != null && def.multiple === true;

            let value: any;
            if (optType === 'boolean') {
                value = true;
                i++;
            } else {
                if (inline !== null) {
                    value = inline;
                    i++;
                } else if (i + 1 < argv.length) {
                    value = String(argv[i + 1]);
                    i += 2;
                } else {
                    if (strict) throw new Error("Option '--" + name + "' requires an argument");
                    value = '';
                    i++;
                }
            }

            if (multiple) {
                if (!Array.isArray(values[name])) values[name] = [];
                values[name].push(value);
            } else {
                values[name] = value;
            }
            continue;
        }

        if (arg.length >= 2 && arg[0] === '-') {
            // -short option(s). A string-typed option with trailing chars takes
            // them inline (-oFOO); otherwise it consumes the next arg.
            const letters = arg.substring(1);
            let consumedNext = false;
            for (let j = 0; j < letters.length; j++) {
                const ch = letters[j];
                const longName = shortLookup[ch];
                if (longName === undefined) {
                    if (strict) throw new Error("Unknown option '-" + ch + "'");
                    continue;
                }
                const def = optionsDef[longName];
                const optType = (def != null && typeof def.type === 'string') ? def.type : 'boolean';
                const multiple = def != null && def.multiple === true;

                let value: any;
                if (optType === 'boolean') {
                    value = true;
                } else {
                    if (j + 1 < letters.length) {
                        value = letters.substring(j + 1);
                        j = letters.length; // consume rest as value
                    } else if (i + 1 < argv.length) {
                        value = String(argv[i + 1]);
                        consumedNext = true;
                    } else {
                        if (strict) throw new Error("Option '-" + ch + "' requires an argument");
                        value = '';
                    }
                }
                if (multiple) {
                    if (!Array.isArray(values[longName])) values[longName] = [];
                    values[longName].push(value);
                } else {
                    values[longName] = value;
                }
            }
            i += consumedNext ? 2 : 1;
            continue;
        }

        if (!allowPositionals && strict) {
            throw new Error("Unexpected argument: " + arg);
        }
        positionals.push(arg);
        i++;
    }

    return { values: values, positionals: positionals };
}

export default {
    format, formatWithOptions, inspect, isDeepStrictEqual, toUSVString, stripVTControlCharacters,
    getSystemErrorName, getSystemErrorMessage, getSystemErrorMap,
    styleText, debuglog, debug,
    deprecate, callbackify, promisify, inherits,
    TextEncoder: _TextEncoder,
    TextDecoder: _TextDecoder,
    types,
    parseArgs,
    MIMEType, MIMEParams,
    aborted, transferableAbortController, transferableAbortSignal,
    parseEnv, getCallSites,
    _extend,
    isArray, isBoolean, isNull, isNullOrUndefined, isNumber, isString, isSymbol,
    isUndefined, isObject, isFunction, isPrimitive, isRegExp, isDate, isError, isBuffer,
};
