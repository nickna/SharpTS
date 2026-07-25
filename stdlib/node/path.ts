// Node.js 'path' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/path.html.
//
// Pure TypeScript implementation of path manipulation. The only host
// dependency is `primitive:process` for `cwd()` (required by resolve)
// and `platform` (used once at module init to pick win32 vs posix
// behavior for the default exports).

import { cwd as __cwd, platform as __platform } from 'primitive:process';

const IS_WIN = __platform === 'win32';

const CHAR_FORWARD_SLASH = 47;  // '/'
const CHAR_BACKWARD_SLASH = 92; // '\\'
const CHAR_COLON = 58;          // ':'
const CHAR_DOT = 46;            // '.'
const CHAR_UPPERCASE_A = 65;    // 'A'
const CHAR_UPPERCASE_Z = 90;    // 'Z'
const CHAR_LOWERCASE_A = 97;    // 'a'
const CHAR_LOWERCASE_Z = 122;   // 'z'

/** Describes a parsed path (the return shape of path.parse). */
export interface ParsedPath {
    root: string;
    dir: string;
    base: string;
    name: string;
    ext: string;
}

// ─── POSIX helpers ──────────────────────────────────────────────────

function isPosixSep(code: number): boolean {
    return code === CHAR_FORWARD_SLASH;
}

function posixIsAbs(p: string): boolean {
    return p.length > 0 && p.charCodeAt(0) === CHAR_FORWARD_SLASH;
}

function normalizeStringPosix(path: string, allowAboveRoot: boolean): string {
    let res = '';
    let lastSegmentLength = 0;
    let lastSlash = -1;
    let dots = 0;
    let code = 0;
    for (let i = 0; i <= path.length; ++i) {
        if (i < path.length) {
            code = path.charCodeAt(i);
        } else if (code === CHAR_FORWARD_SLASH) {
            break;
        } else {
            code = CHAR_FORWARD_SLASH;
        }
        if (code === CHAR_FORWARD_SLASH) {
            if (lastSlash === i - 1 || dots === 1) {
                // noop
            } else if (dots === 2) {
                if (res.length < 2 || lastSegmentLength !== 2 ||
                    res.charCodeAt(res.length - 1) !== CHAR_DOT ||
                    res.charCodeAt(res.length - 2) !== CHAR_DOT) {
                    if (res.length > 2) {
                        const lastSlashIndex = res.lastIndexOf('/');
                        if (lastSlashIndex === -1) {
                            res = '';
                            lastSegmentLength = 0;
                        } else {
                            res = res.slice(0, lastSlashIndex);
                            lastSegmentLength = res.length - 1 - res.lastIndexOf('/');
                        }
                        lastSlash = i;
                        dots = 0;
                        continue;
                    } else if (res.length !== 0) {
                        res = '';
                        lastSegmentLength = 0;
                        lastSlash = i;
                        dots = 0;
                        continue;
                    }
                }
                if (allowAboveRoot) {
                    res = res.length > 0 ? res + '/..' : '..';
                    lastSegmentLength = 2;
                }
            } else {
                if (res.length > 0) {
                    res = res + '/' + path.slice(lastSlash + 1, i);
                } else {
                    res = path.slice(lastSlash + 1, i);
                }
                lastSegmentLength = i - lastSlash - 1;
            }
            lastSlash = i;
            dots = 0;
        } else if (code === CHAR_DOT && dots !== -1) {
            ++dots;
        } else {
            dots = -1;
        }
    }
    return res;
}

function posixNormalize(p: string): string {
    if (p.length === 0) return '.';
    const isAbsolute = posixIsAbs(p);
    const trailingSeparator = p.charCodeAt(p.length - 1) === CHAR_FORWARD_SLASH;
    let path = normalizeStringPosix(p, !isAbsolute);
    if (path.length === 0) {
        if (isAbsolute) return '/';
        return trailingSeparator ? './' : '.';
    }
    if (trailingSeparator) path = path + '/';
    return isAbsolute ? '/' + path : path;
}

function posixJoin(parts: string[]): string {
    if (parts.length === 0) return '.';
    let joined = '';
    for (let i = 0; i < parts.length; i++) {
        const arg = parts[i];
        if (arg.length > 0) {
            if (joined.length === 0) joined = arg;
            else joined = joined + '/' + arg;
        }
    }
    if (joined.length === 0) return '.';
    return posixNormalize(joined);
}

function posixResolve(parts: string[]): string {
    let resolvedPath = '';
    let resolvedAbsolute = false;
    for (let i = parts.length - 1; i >= -1 && !resolvedAbsolute; i--) {
        const path = i >= 0 ? parts[i] : __cwd();
        if (path.length === 0) continue;
        resolvedPath = path + '/' + resolvedPath;
        resolvedAbsolute = posixIsAbs(path);
    }
    resolvedPath = normalizeStringPosix(resolvedPath, !resolvedAbsolute);
    if (resolvedAbsolute) return '/' + resolvedPath;
    return resolvedPath.length > 0 ? resolvedPath : '.';
}

function posixBasename(p: string, ext?: string): string {
    let start = 0;
    let end = -1;
    let matchedSlash = true;
    if (ext != null && ext.length > 0 && ext.length <= p.length) {
        if (ext === p) return '';
        let extIdx = ext.length - 1;
        let firstNonSlashEnd = -1;
        for (let i = p.length - 1; i >= 0; --i) {
            const code = p.charCodeAt(i);
            if (code === CHAR_FORWARD_SLASH) {
                if (!matchedSlash) { start = i + 1; break; }
            } else {
                if (firstNonSlashEnd === -1) { matchedSlash = false; firstNonSlashEnd = i + 1; }
                if (extIdx >= 0) {
                    if (code === ext.charCodeAt(extIdx)) {
                        if (--extIdx === -1) end = i;
                    } else {
                        extIdx = -1;
                        end = firstNonSlashEnd;
                    }
                }
            }
        }
        if (start === end) end = firstNonSlashEnd;
        else if (end === -1) end = p.length;
        return p.slice(start, end);
    }
    for (let i = p.length - 1; i >= 0; --i) {
        if (p.charCodeAt(i) === CHAR_FORWARD_SLASH) {
            if (!matchedSlash) { start = i + 1; break; }
        } else if (end === -1) {
            matchedSlash = false;
            end = i + 1;
        }
    }
    return end === -1 ? '' : p.slice(start, end);
}

function posixDirname(p: string): string {
    if (p.length === 0) return '.';
    const hasRoot = p.charCodeAt(0) === CHAR_FORWARD_SLASH;
    let end = -1;
    let matchedSlash = true;
    for (let i = p.length - 1; i >= 1; --i) {
        if (p.charCodeAt(i) === CHAR_FORWARD_SLASH) {
            if (!matchedSlash) { end = i; break; }
        } else {
            matchedSlash = false;
        }
    }
    if (end === -1) return hasRoot ? '/' : '.';
    if (hasRoot && end === 1) return '//';
    return p.slice(0, end);
}

function extnameGeneric(p: string, win32: boolean): string {
    let startDot = -1;
    let startPart = 0;
    let end = -1;
    let matchedSlash = true;
    let preDotState = 0;
    for (let i = p.length - 1; i >= 0; --i) {
        const code = p.charCodeAt(i);
        const isSep = win32 ? (code === CHAR_FORWARD_SLASH || code === CHAR_BACKWARD_SLASH) : (code === CHAR_FORWARD_SLASH);
        if (isSep) {
            if (!matchedSlash) { startPart = i + 1; break; }
            continue;
        }
        if (end === -1) { matchedSlash = false; end = i + 1; }
        if (code === CHAR_DOT) {
            if (startDot === -1) startDot = i;
            else if (preDotState !== 1) preDotState = 1;
        } else if (startDot !== -1) {
            preDotState = -1;
        }
    }
    if (startDot === -1 || end === -1 || preDotState === 0 || (preDotState === 1 && startDot === end - 1 && startDot === startPart + 1)) {
        return '';
    }
    return p.slice(startDot, end);
}

function posixIsAbsolute(p: string): boolean {
    return posixIsAbs(p);
}

function posixRelative(from: string, to: string): string {
    if (from === to) return '';
    from = posixResolve([from]);
    to = posixResolve([to]);
    if (from === to) return '';
    let fromStart = 1;
    while (fromStart < from.length && from.charCodeAt(fromStart) === CHAR_FORWARD_SLASH) fromStart++;
    const fromEnd = from.length;
    const fromLen = fromEnd - fromStart;
    let toStart = 1;
    while (toStart < to.length && to.charCodeAt(toStart) === CHAR_FORWARD_SLASH) toStart++;
    const toEnd = to.length;
    const toLen = toEnd - toStart;
    const length = fromLen < toLen ? fromLen : toLen;
    let lastCommonSep = -1;
    let i = 0;
    for (; i < length; i++) {
        const fromCode = from.charCodeAt(fromStart + i);
        if (fromCode !== to.charCodeAt(toStart + i)) break;
        if (fromCode === CHAR_FORWARD_SLASH) lastCommonSep = i;
    }
    if (i === length) {
        if (toLen > length) {
            if (to.charCodeAt(toStart + i) === CHAR_FORWARD_SLASH) return to.slice(toStart + i + 1);
            if (i === 0) return to.slice(toStart + i);
        } else if (fromLen > length) {
            if (from.charCodeAt(fromStart + i) === CHAR_FORWARD_SLASH) lastCommonSep = i;
            else if (i === 0) lastCommonSep = 0;
        }
    }
    let out = '';
    for (i = fromStart + lastCommonSep + 1; i <= fromEnd; ++i) {
        if (i === fromEnd || from.charCodeAt(i) === CHAR_FORWARD_SLASH) {
            out = out.length === 0 ? '..' : out + '/..';
        }
    }
    const tail = to.slice(toStart + lastCommonSep + (out.length > 0 ? 0 : 1));
    if (out.length === 0) return tail;
    return tail.length === 0 ? out : out + tail;
}

function posixParse(p: string): ParsedPath {
    const ret: ParsedPath = { root: '', dir: '', base: '', ext: '', name: '' };
    if (p.length === 0) return ret;
    const isAbsolute = p.charCodeAt(0) === CHAR_FORWARD_SLASH;
    let start: number;
    if (isAbsolute) { ret.root = '/'; start = 1; } else { start = 0; }
    let startDot = -1;
    let startPart = 0;
    let end = -1;
    let matchedSlash = true;
    let i = p.length - 1;
    let preDotState = 0;
    for (; i >= start; --i) {
        const code = p.charCodeAt(i);
        if (code === CHAR_FORWARD_SLASH) {
            if (!matchedSlash) { startPart = i + 1; break; }
            continue;
        }
        if (end === -1) { matchedSlash = false; end = i + 1; }
        if (code === CHAR_DOT) {
            if (startDot === -1) startDot = i;
            else if (preDotState !== 1) preDotState = 1;
        } else if (startDot !== -1) {
            preDotState = -1;
        }
    }
    if (end !== -1) {
        const s = startPart === 0 && isAbsolute ? 1 : startPart;
        if (startDot === -1 || end === -1 || preDotState === 0 || (preDotState === 1 && startDot === end - 1 && startDot === startPart + 1)) {
            ret.base = p.slice(s, end);
            ret.name = ret.base;
        } else {
            ret.name = p.slice(s, startDot);
            ret.base = p.slice(s, end);
            ret.ext = p.slice(startDot, end);
        }
    }
    if (startPart > 0) ret.dir = p.slice(0, startPart - 1);
    else if (isAbsolute) ret.dir = '/';
    return ret;
}

function formatGeneric(sep: string, obj: ParsedPath): string {
    const dir = obj.dir || obj.root || '';
    const base = obj.base || ((obj.name || '') + (obj.ext || ''));
    if (dir.length === 0) return base;
    if (dir === obj.root) return dir + base;
    return dir + sep + base;
}

// ─── Win32 helpers ──────────────────────────────────────────────────

function isWin32Sep(code: number): boolean {
    return code === CHAR_FORWARD_SLASH || code === CHAR_BACKWARD_SLASH;
}

function isDriveLetter(code: number): boolean {
    return (code >= CHAR_UPPERCASE_A && code <= CHAR_UPPERCASE_Z)
        || (code >= CHAR_LOWERCASE_A && code <= CHAR_LOWERCASE_Z);
}

function win32IsAbsolute(p: string): boolean {
    // Any leading separator is absolute (Node treats '/foo' and '\foo' as
    // rooted on win32), as is a drive letter followed by a separator.
    // Note 'C:' alone is *not* absolute — it is drive-relative.
    const len = p.length;
    if (len === 0) return false;
    const c0 = p.charCodeAt(0);
    if (isWin32Sep(c0)) return true;
    return isDriveLetter(c0) && len > 2
        && p.charCodeAt(1) === CHAR_COLON && isWin32Sep(p.charCodeAt(2));
}

function normalizeStringWin32(path: string, allowAboveRoot: boolean, separator: string): string {
    let res = '';
    let lastSegmentLength = 0;
    let lastSlash = -1;
    let dots = 0;
    let code = 0;
    for (let i = 0; i <= path.length; ++i) {
        if (i < path.length) {
            code = path.charCodeAt(i);
        } else if (isWin32Sep(code)) {
            break;
        } else {
            code = CHAR_BACKWARD_SLASH;
        }
        if (isWin32Sep(code)) {
            if (lastSlash === i - 1 || dots === 1) {
                // noop
            } else if (dots === 2) {
                if (res.length < 2 || lastSegmentLength !== 2 ||
                    res.charCodeAt(res.length - 1) !== CHAR_DOT ||
                    res.charCodeAt(res.length - 2) !== CHAR_DOT) {
                    if (res.length > 2) {
                        const lastSlashIndex = res.lastIndexOf(separator);
                        if (lastSlashIndex === -1) {
                            res = '';
                            lastSegmentLength = 0;
                        } else {
                            res = res.slice(0, lastSlashIndex);
                            lastSegmentLength = res.length - 1 - res.lastIndexOf(separator);
                        }
                        lastSlash = i;
                        dots = 0;
                        continue;
                    } else if (res.length !== 0) {
                        res = '';
                        lastSegmentLength = 0;
                        lastSlash = i;
                        dots = 0;
                        continue;
                    }
                }
                if (allowAboveRoot) {
                    res = res.length > 0 ? res + separator + '..' : '..';
                    lastSegmentLength = 2;
                }
            } else {
                if (res.length > 0) {
                    res = res + separator + path.slice(lastSlash + 1, i);
                } else {
                    res = path.slice(lastSlash + 1, i);
                }
                lastSegmentLength = i - lastSlash - 1;
            }
            lastSlash = i;
            dots = 0;
        } else if (code === CHAR_DOT && dots !== -1) {
            ++dots;
        } else {
            dots = -1;
        }
    }
    return res;
}

function win32Normalize(p: string): string {
    const len = p.length;
    if (len === 0) return '.';
    let rootEnd = 0;
    let device = '';
    let isAbsolute = false;
    const code = p.charCodeAt(0);

    // Single character: '/' normalizes to '\', anything else is already normal.
    if (len === 1) return code === CHAR_FORWARD_SLASH ? '\\' : p;

    if (isWin32Sep(code)) {
        // A leading separator means absolute — UNC or otherwise.
        isAbsolute = true;

        if (isWin32Sep(p.charCodeAt(1))) {
            // Possible UNC root: \\server\share\...
            let j = 2;
            let last = j;
            // Match server
            while (j < len && !isWin32Sep(p.charCodeAt(j))) j++;
            if (j < len && j !== last) {
                const firstPart = p.slice(last, j);
                last = j;
                while (j < len && isWin32Sep(p.charCodeAt(j))) j++;
                if (j < len && j !== last) {
                    last = j;
                    // Match share
                    while (j < len && !isWin32Sep(p.charCodeAt(j))) j++;
                    if (j === len) {
                        // A UNC root and nothing else — already normalized.
                        return '\\\\' + firstPart + '\\' + p.slice(last) + '\\';
                    }
                    if (j !== last) {
                        device = '\\\\' + firstPart + '\\' + p.slice(last, j);
                        rootEnd = j;
                    }
                }
            }
        } else {
            rootEnd = 1;
        }
    } else if (isDriveLetter(code) && p.charCodeAt(1) === CHAR_COLON) {
        device = p.slice(0, 2);
        rootEnd = 2;
        if (len > 2 && isWin32Sep(p.charCodeAt(2))) {
            isAbsolute = true;
            rootEnd = 3;
        }
    }

    let tail = rootEnd < len ? normalizeStringWin32(p.slice(rootEnd), !isAbsolute, '\\') : '';
    if (tail.length === 0 && !isAbsolute) tail = '.';
    if (tail.length > 0 && isWin32Sep(p.charCodeAt(len - 1))) tail = tail + '\\';
    if (device.length === 0) {
        return isAbsolute ? '\\' + tail : tail;
    }
    return isAbsolute ? device + '\\' + tail : device + tail;
}

function win32Join(parts: string[]): string {
    if (parts.length === 0) return '.';
    let joined: string | undefined;
    let firstPart: string | undefined;
    for (let i = 0; i < parts.length; ++i) {
        const arg = parts[i];
        if (arg.length > 0) {
            if (joined === undefined) { joined = firstPart = arg; }
            else joined = joined + '\\' + arg;
        }
    }
    if (joined === undefined) return '.';
    // Handle the edge case around UNC leading-slash normalization briefly.
    let needsReplace = true;
    let slashCount = 0;
    if (firstPart !== undefined && isWin32Sep(firstPart.charCodeAt(0))) {
        ++slashCount;
        const firstLen = firstPart.length;
        if (firstLen > 1 && isWin32Sep(firstPart.charCodeAt(1))) {
            ++slashCount;
            if (firstLen > 2) {
                if (isWin32Sep(firstPart.charCodeAt(2))) ++slashCount;
                else needsReplace = false;
            }
        }
    }
    if (needsReplace) {
        while (slashCount < joined.length && isWin32Sep(joined.charCodeAt(slashCount))) ++slashCount;
        if (slashCount >= 2) joined = '\\' + joined.slice(slashCount);
    }
    return win32Normalize(joined);
}

function win32Resolve(parts: string[]): string {
    let resolvedDevice = '';
    let resolvedTail = '';
    let resolvedAbsolute = false;
    for (let i = parts.length - 1; i >= -1; i--) {
        let path: string;
        if (i >= 0) path = parts[i];
        else if (resolvedDevice.length === 0) path = __cwd();
        else {
            // When no cwd-qualifier is available for a device, fall back to cwd.
            path = __cwd();
            if (path.length < 3 || path.slice(0, 2).toLowerCase() !== resolvedDevice.toLowerCase() || path.charCodeAt(2) !== CHAR_BACKWARD_SLASH) {
                path = resolvedDevice + '\\';
            }
        }
        if (path.length === 0) continue;
        const len = path.length;
        let rootEnd = 0;
        let device = '';
        let isAbsolute = false;
        const code = path.charCodeAt(0);
        if (len === 1) {
            if (isWin32Sep(code)) { rootEnd = 1; isAbsolute = true; }
        } else if (isWin32Sep(code)) {
            isAbsolute = true;
            if (isWin32Sep(path.charCodeAt(1))) {
                let j = 2;
                let last = j;
                while (j < len && !isWin32Sep(path.charCodeAt(j))) j++;
                if (j < len && j !== last) {
                    const firstPart = path.slice(last, j);
                    last = j;
                    while (j < len && isWin32Sep(path.charCodeAt(j))) j++;
                    if (j < len && j !== last) {
                        last = j;
                        while (j < len && !isWin32Sep(path.charCodeAt(j))) j++;
                        if (j === len || j !== last) {
                            device = '\\\\' + firstPart + '\\' + path.slice(last, j);
                            rootEnd = j;
                        }
                    }
                }
            } else rootEnd = 1;
        } else if (isDriveLetter(code) && path.charCodeAt(1) === CHAR_COLON) {
            device = path.slice(0, 2);
            rootEnd = 2;
            if (len > 2 && isWin32Sep(path.charCodeAt(2))) { isAbsolute = true; rootEnd = 3; }
        }
        if (device.length > 0) {
            if (resolvedDevice.length > 0) {
                if (device.toLowerCase() !== resolvedDevice.toLowerCase()) continue;
            } else {
                resolvedDevice = device;
            }
        }
        if (resolvedAbsolute) {
            if (resolvedDevice.length > 0) break;
        } else {
            resolvedTail = path.slice(rootEnd) + '\\' + resolvedTail;
            resolvedAbsolute = isAbsolute;
            if (isAbsolute && resolvedDevice.length > 0) break;
        }
    }
    resolvedTail = normalizeStringWin32(resolvedTail, !resolvedAbsolute, '\\');
    return resolvedAbsolute ? resolvedDevice + '\\' + resolvedTail
        : resolvedDevice + resolvedTail || '.';
}

function win32Basename(p: string, ext?: string): string {
    let start = 0;
    let end = -1;
    let matchedSlash = true;
    // Skip leading drive letter
    if (p.length >= 2 && isDriveLetter(p.charCodeAt(0)) && p.charCodeAt(1) === CHAR_COLON) start = 2;

    if (ext != null && ext.length > 0 && ext.length <= p.length) {
        if (ext === p) return '';
        let extIdx = ext.length - 1;
        let firstNonSlashEnd = -1;
        for (let i = p.length - 1; i >= start; --i) {
            const code = p.charCodeAt(i);
            if (isWin32Sep(code)) {
                if (!matchedSlash) { start = i + 1; break; }
            } else {
                if (firstNonSlashEnd === -1) { matchedSlash = false; firstNonSlashEnd = i + 1; }
                if (extIdx >= 0) {
                    if (code === ext.charCodeAt(extIdx)) {
                        if (--extIdx === -1) end = i;
                    } else {
                        extIdx = -1;
                        end = firstNonSlashEnd;
                    }
                }
            }
        }
        if (start === end) end = firstNonSlashEnd;
        else if (end === -1) end = p.length;
        return p.slice(start, end);
    }
    for (let i = p.length - 1; i >= start; --i) {
        if (isWin32Sep(p.charCodeAt(i))) {
            if (!matchedSlash) { start = i + 1; break; }
        } else if (end === -1) {
            matchedSlash = false;
            end = i + 1;
        }
    }
    return end === -1 ? '' : p.slice(start, end);
}

function win32Dirname(p: string): string {
    const len = p.length;
    if (len === 0) return '.';
    let rootEnd = -1;
    let offset = 0;
    const code = p.charCodeAt(0);

    if (len === 1) return isWin32Sep(code) ? p : '.';
    if (isWin32Sep(code)) {
        rootEnd = offset = 1;
        if (isWin32Sep(p.charCodeAt(1))) {
            let j = 2;
            let last = j;
            while (j < len && !isWin32Sep(p.charCodeAt(j))) j++;
            if (j < len && j !== last) {
                last = j;
                while (j < len && isWin32Sep(p.charCodeAt(j))) j++;
                if (j < len && j !== last) {
                    last = j;
                    while (j < len && !isWin32Sep(p.charCodeAt(j))) j++;
                    if (j === len) return p;
                    if (j !== last) rootEnd = offset = j + 1;
                }
            }
        }
    } else if (isDriveLetter(code) && p.charCodeAt(1) === CHAR_COLON) {
        rootEnd = len > 2 && isWin32Sep(p.charCodeAt(2)) ? 3 : 2;
        offset = rootEnd;
    }
    let end = -1;
    let matchedSlash = true;
    for (let i = len - 1; i >= offset; --i) {
        if (isWin32Sep(p.charCodeAt(i))) {
            if (!matchedSlash) { end = i; break; }
        } else matchedSlash = false;
    }
    if (end === -1) {
        if (rootEnd === -1) return '.';
        end = rootEnd;
    }
    return p.slice(0, end);
}

function win32Relative(from: string, to: string): string {
    if (from === to) return '';
    const fromOrig = win32Resolve([from]);
    const toOrig = win32Resolve([to]);
    if (fromOrig === toOrig) return '';
    from = fromOrig.toLowerCase();
    to = toOrig.toLowerCase();
    if (from === to) return '';
    let fromStart = 0;
    while (fromStart < from.length && from.charCodeAt(fromStart) === CHAR_BACKWARD_SLASH) fromStart++;
    const fromEnd = from.length;
    const fromLen = fromEnd - fromStart;
    let toStart = 0;
    while (toStart < to.length && to.charCodeAt(toStart) === CHAR_BACKWARD_SLASH) toStart++;
    const toEnd = to.length;
    const toLen = toEnd - toStart;
    const length = fromLen < toLen ? fromLen : toLen;
    let lastCommonSep = -1;
    let i = 0;
    for (; i < length; i++) {
        const fromCode = from.charCodeAt(fromStart + i);
        if (fromCode !== to.charCodeAt(toStart + i)) break;
        if (fromCode === CHAR_BACKWARD_SLASH) lastCommonSep = i;
    }
    if (i === length) {
        if (toLen > length) {
            if (to.charCodeAt(toStart + i) === CHAR_BACKWARD_SLASH) return toOrig.slice(toStart + i + 1);
            if (i === 0) return toOrig.slice(toStart + i);
        } else if (fromLen > length) {
            if (from.charCodeAt(fromStart + i) === CHAR_BACKWARD_SLASH) lastCommonSep = i;
            else if (i === 0) lastCommonSep = 0;
        }
    }
    let out = '';
    for (i = fromStart + lastCommonSep + 1; i <= fromEnd; ++i) {
        if (i === fromEnd || from.charCodeAt(i) === CHAR_BACKWARD_SLASH) {
            out = out.length === 0 ? '..' : out + '\\..';
        }
    }
    const tail = toOrig.slice(toStart + lastCommonSep, toEnd);
    if (out.length === 0) {
        let s = 0;
        if (tail.length > 0 && tail.charCodeAt(0) === CHAR_BACKWARD_SLASH) s = 1;
        return tail.slice(s);
    }
    return tail.length > 0 ? out + tail : out;
}

function win32Parse(p: string): ParsedPath {
    const ret: ParsedPath = { root: '', dir: '', base: '', ext: '', name: '' };
    if (p.length === 0) return ret;
    const len = p.length;
    let rootEnd = 0;
    let code = p.charCodeAt(0);
    if (len === 1) {
        if (isWin32Sep(code)) { ret.root = ret.dir = p; return ret; }
        ret.base = ret.name = p;
        return ret;
    }
    if (isWin32Sep(code)) {
        rootEnd = 1;
        if (isWin32Sep(p.charCodeAt(1))) {
            let j = 2;
            let last = j;
            while (j < len && !isWin32Sep(p.charCodeAt(j))) j++;
            if (j < len && j !== last) {
                last = j;
                while (j < len && isWin32Sep(p.charCodeAt(j))) j++;
                if (j < len && j !== last) {
                    last = j;
                    while (j < len && !isWin32Sep(p.charCodeAt(j))) j++;
                    if (j === len) rootEnd = j;
                    else if (j !== last) rootEnd = j + 1;
                }
            }
        }
    } else if (isDriveLetter(code) && p.charCodeAt(1) === CHAR_COLON) {
        if (len <= 2) { ret.root = ret.dir = p; return ret; }
        rootEnd = 2;
        if (isWin32Sep(p.charCodeAt(2))) {
            if (len === 3) { ret.root = ret.dir = p; return ret; }
            rootEnd = 3;
        }
    }
    if (rootEnd > 0) ret.root = p.slice(0, rootEnd);
    let startDot = -1;
    let startPart = rootEnd;
    let end = -1;
    let matchedSlash = true;
    let i = p.length - 1;
    let preDotState = 0;
    for (; i >= rootEnd; --i) {
        code = p.charCodeAt(i);
        if (isWin32Sep(code)) {
            if (!matchedSlash) { startPart = i + 1; break; }
            continue;
        }
        if (end === -1) { matchedSlash = false; end = i + 1; }
        if (code === CHAR_DOT) {
            if (startDot === -1) startDot = i;
            else if (preDotState !== 1) preDotState = 1;
        } else if (startDot !== -1) {
            preDotState = -1;
        }
    }
    if (end !== -1) {
        if (startDot === -1 || preDotState === 0 || (preDotState === 1 && startDot === end - 1 && startDot === startPart + 1)) {
            ret.base = p.slice(startPart, end);
            ret.name = ret.base;
        } else {
            ret.name = p.slice(startPart, startDot);
            ret.base = p.slice(startPart, end);
            ret.ext = p.slice(startDot, end);
        }
    }
    if (startPart > 0 && startPart !== rootEnd) ret.dir = p.slice(0, startPart - 1);
    else ret.dir = ret.root;
    return ret;
}

// ─── POSIX namespace ────────────────────────────────────────────────

export const posix = {
    sep: '/',
    delimiter: ':',
    join(...parts: string[]): string { return posixJoin(parts); },
    resolve(...parts: string[]): string { return posixResolve(parts); },
    basename(p: string, ext?: string): string { return posixBasename(p, ext); },
    dirname(p: string): string { return posixDirname(p); },
    extname(p: string): string { return extnameGeneric(p, false); },
    normalize(p: string): string { return posixNormalize(p); },
    isAbsolute(p: string): boolean { return posixIsAbsolute(p); },
    relative(from: string, to: string): string { return posixRelative(from, to); },
    parse(p: string): ParsedPath { return posixParse(p); },
    format(obj: ParsedPath): string { return formatGeneric('/', obj); },
};

// ─── Win32 namespace ────────────────────────────────────────────────

export const win32 = {
    sep: '\\',
    delimiter: ';',
    join(...parts: string[]): string { return win32Join(parts); },
    resolve(...parts: string[]): string { return win32Resolve(parts); },
    basename(p: string, ext?: string): string { return win32Basename(p, ext); },
    dirname(p: string): string { return win32Dirname(p); },
    extname(p: string): string { return extnameGeneric(p, true); },
    normalize(p: string): string { return win32Normalize(p); },
    isAbsolute(p: string): boolean { return win32IsAbsolute(p); },
    relative(from: string, to: string): string { return win32Relative(from, to); },
    parse(p: string): ParsedPath { return win32Parse(p); },
    format(obj: ParsedPath): string { return formatGeneric('\\', obj); },
};

// ─── Default (platform-conditioned) exports ─────────────────────────

export const sep: string = IS_WIN ? '\\' : '/';
export const delimiter: string = IS_WIN ? ';' : ':';

export function join(...parts: string[]): string {
    return IS_WIN ? win32Join(parts) : posixJoin(parts);
}

export function resolve(...parts: string[]): string {
    return IS_WIN ? win32Resolve(parts) : posixResolve(parts);
}

export function basename(p: string, ext?: string): string {
    return IS_WIN ? win32Basename(p, ext) : posixBasename(p, ext);
}

export function dirname(p: string): string {
    return IS_WIN ? win32Dirname(p) : posixDirname(p);
}

export function extname(p: string): string {
    return extnameGeneric(p, IS_WIN);
}

export function normalize(p: string): string {
    return IS_WIN ? win32Normalize(p) : posixNormalize(p);
}

export function isAbsolute(p: string): boolean {
    return IS_WIN ? win32IsAbsolute(p) : posixIsAbsolute(p);
}

export function relative(from: string, to: string): string {
    return IS_WIN ? win32Relative(from, to) : posixRelative(from, to);
}

export function parse(p: string): ParsedPath {
    return IS_WIN ? win32Parse(p) : posixParse(p);
}

export function format(obj: ParsedPath): string {
    return formatGeneric(IS_WIN ? '\\' : '/', obj);
}

export default {
    posix, win32, sep, delimiter,
    join, resolve, basename, dirname, extname, normalize,
    isAbsolute, relative, parse, format,
};
