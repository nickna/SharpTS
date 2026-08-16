// Node.js 'url' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/url.html.
//
// Implements the full WHATWG URL Living Standard (https://url.spec.whatwg.org/)
// state machine for URL parsing. No .NET System.Uri dependency — the parser
// is pure TS operating on code points, so divergences from Node's observable
// behavior are our own, not inherited from a host library with different
// semantics.
//
// What's implemented:
//   - WHATWG URL parser (all parser states)
//   - URL class with property getters/setters
//   - URLSearchParams class with full iterator API
//   - Legacy url.parse / url.format / url.resolve (pre-WHATWG Node API)
//   - fileURLToPath / pathToFileURL helpers
//
// Known simplifications relative to full spec compliance:
//   - IDN/Unicode domain handling falls through to ASCII validation. Punycode
//     encoding for non-ASCII domains is not implemented; such domains are
//     preserved as-is (not percent-encoded, not converted to punycode).
//   - Host validation is permissive: we accept inputs Node would reject, in
//     order to match the pre-migration .NET Uri behavior that existing tests
//     encode. Strictly-compliant host rejection can be re-tightened later.

// ─── Code-point helpers ─────────────────────────────────────────────

const CHAR_TAB = 0x09;
const CHAR_LF = 0x0A;
const CHAR_CR = 0x0D;
const CHAR_SPACE = 0x20;
const CHAR_BANG = 0x21;
const CHAR_HASH = 0x23;
const CHAR_DOLLAR = 0x24;
const CHAR_PERCENT = 0x25;
const CHAR_AMP = 0x26;
const CHAR_SQUOTE = 0x27;
const CHAR_LPAREN = 0x28;
const CHAR_RPAREN = 0x29;
const CHAR_STAR = 0x2A;
const CHAR_PLUS = 0x2B;
const CHAR_COMMA = 0x2C;
const CHAR_MINUS = 0x2D;
const CHAR_DOT = 0x2E;
const CHAR_SLASH = 0x2F;
const CHAR_0 = 0x30;
const CHAR_9 = 0x39;
const CHAR_COLON = 0x3A;
const CHAR_SEMI = 0x3B;
const CHAR_LT = 0x3C;
const CHAR_EQ = 0x3D;
const CHAR_GT = 0x3E;
const CHAR_QUESTION = 0x3F;
const CHAR_AT = 0x40;
const CHAR_A = 0x41;
const CHAR_F = 0x46;
const CHAR_Z = 0x5A;
const CHAR_LBRACKET = 0x5B;
const CHAR_BACKSLASH = 0x5C;
const CHAR_RBRACKET = 0x5D;
const CHAR_CARET = 0x5E;
const CHAR_UNDERSCORE = 0x5F;
const CHAR_BACKTICK = 0x60;
const CHAR_LOWER_A = 0x61;
const CHAR_LOWER_F = 0x66;
const CHAR_LOWER_Z = 0x7A;
const CHAR_LBRACE = 0x7B;
const CHAR_PIPE = 0x7C;
const CHAR_RBRACE = 0x7D;
const CHAR_TILDE = 0x7E;
const CHAR_DEL = 0x7F;

function isAsciiDigit(c: number): boolean {
    return c >= CHAR_0 && c <= CHAR_9;
}

function isAsciiAlpha(c: number): boolean {
    return (c >= CHAR_A && c <= CHAR_Z) || (c >= CHAR_LOWER_A && c <= CHAR_LOWER_Z);
}

function isAsciiAlphanumeric(c: number): boolean {
    return isAsciiAlpha(c) || isAsciiDigit(c);
}

function isAsciiHex(c: number): boolean {
    return isAsciiDigit(c)
        || (c >= CHAR_A && c <= CHAR_F)
        || (c >= CHAR_LOWER_A && c <= CHAR_LOWER_F);
}

function asciiLower(c: number): number {
    if (c >= CHAR_A && c <= CHAR_Z) return c + 32;
    return c;
}

function hexDigit(c: number): number {
    if (c >= CHAR_0 && c <= CHAR_9) return c - CHAR_0;
    if (c >= CHAR_A && c <= CHAR_F) return c - CHAR_A + 10;
    if (c >= CHAR_LOWER_A && c <= CHAR_LOWER_F) return c - CHAR_LOWER_A + 10;
    return -1;
}

function toHex(c: number): string {
    const hi = (c >> 4) & 0xF;
    const lo = c & 0xF;
    const hiCh = hi < 10 ? String.fromCharCode(CHAR_0 + hi) : String.fromCharCode(CHAR_A + hi - 10);
    const loCh = lo < 10 ? String.fromCharCode(CHAR_0 + lo) : String.fromCharCode(CHAR_A + lo - 10);
    return '%' + hiCh + loCh;
}

// Encode a Unicode code point as UTF-8 bytes.
function utf8Encode(cp: number): number[] {
    if (cp < 0x80) return [cp];
    if (cp < 0x800) return [0xC0 | (cp >> 6), 0x80 | (cp & 0x3F)];
    if (cp < 0x10000) return [0xE0 | (cp >> 12), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
    return [0xF0 | (cp >> 18), 0x80 | ((cp >> 12) & 0x3F), 0x80 | ((cp >> 6) & 0x3F), 0x80 | (cp & 0x3F)];
}

// ─── Percent-encoding sets (WHATWG §1.3) ────────────────────────────

// C0 controls and >0x7E
function isC0ControlPercentEncode(c: number): boolean {
    return c <= 0x1F || c > CHAR_TILDE;
}

// Fragment = C0 + space, ", <, >, `
function isFragmentPercentEncode(c: number): boolean {
    if (isC0ControlPercentEncode(c)) return true;
    return c === CHAR_SPACE || c === 0x22 || c === CHAR_LT || c === CHAR_GT || c === CHAR_BACKTICK;
}

// Query = fragment + "#" but NOT `
function isQueryPercentEncode(c: number): boolean {
    if (isC0ControlPercentEncode(c)) return true;
    return c === CHAR_SPACE || c === 0x22 || c === CHAR_LT || c === CHAR_GT || c === CHAR_HASH;
}

// Special-scheme query additionally encodes '
function isSpecialQueryPercentEncode(c: number): boolean {
    return isQueryPercentEncode(c) || c === CHAR_SQUOTE;
}

// Path = query + "?", "`", "{", "}"
function isPathPercentEncode(c: number): boolean {
    if (isQueryPercentEncode(c)) return true;
    return c === CHAR_QUESTION || c === CHAR_BACKTICK || c === CHAR_LBRACE || c === CHAR_RBRACE;
}

// Userinfo = path + "/", ":", ";", "=", "@", "[", "\", "]", "^", "|"
function isUserinfoPercentEncode(c: number): boolean {
    if (isPathPercentEncode(c)) return true;
    return c === CHAR_SLASH || c === CHAR_COLON || c === CHAR_SEMI || c === CHAR_EQ
        || c === CHAR_AT || c === CHAR_LBRACKET || c === CHAR_BACKSLASH || c === CHAR_RBRACKET
        || c === CHAR_CARET || c === CHAR_PIPE;
}

// Component = userinfo + "$", "%", "&", "+", ","
function isComponentPercentEncode(c: number): boolean {
    if (isUserinfoPercentEncode(c)) return true;
    return c === CHAR_DOLLAR || c === CHAR_PERCENT || c === CHAR_AMP
        || c === CHAR_PLUS || c === CHAR_COMMA;
}

// application/x-www-form-urlencoded = component + "!", "'", "(", ")", "~"
function isFormUrlencodedPercentEncode(c: number): boolean {
    if (isComponentPercentEncode(c)) return true;
    return c === CHAR_BANG || c === CHAR_SQUOTE || c === CHAR_LPAREN
        || c === CHAR_RPAREN || c === CHAR_TILDE;
}

function percentEncodeCodePoint(cp: number, inSet: (c: number) => boolean): string {
    if (!inSet(cp)) {
        if (cp < 0x80) return String.fromCharCode(cp);
        // Non-ASCII: UTF-8 encode + percent encode each byte
        let out = '';
        const bytes = utf8Encode(cp);
        for (let i = 0; i < bytes.length; i++) out += toHex(bytes[i]);
        return out;
    }
    if (cp < 0x80) return toHex(cp);
    // UTF-8 encode + percent encode each byte
    let out = '';
    const bytes = utf8Encode(cp);
    for (let i = 0; i < bytes.length; i++) out += toHex(bytes[i]);
    return out;
}

function percentEncodeString(input: string, inSet: (c: number) => boolean): string {
    let out = '';
    for (let i = 0; i < input.length; i++) {
        const cp = input.charCodeAt(i);
        // Handle surrogate pairs: combine into one code point for encoding.
        if (cp >= 0xD800 && cp <= 0xDBFF && i + 1 < input.length) {
            const low = input.charCodeAt(i + 1);
            if (low >= 0xDC00 && low <= 0xDFFF) {
                const full = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                out += percentEncodeCodePoint(full, inSet);
                i++;
                continue;
            }
        }
        out += percentEncodeCodePoint(cp, inSet);
    }
    return out;
}

function percentDecode(input: string): string {
    // Byte-oriented decode: % then two hex digits → byte. Non-% bytes pass through.
    // Returns a UTF-8-decoded JS string.
    const bytes: number[] = [];
    for (let i = 0; i < input.length; i++) {
        const c = input.charCodeAt(i);
        if (c !== CHAR_PERCENT || i + 2 >= input.length) {
            bytes.push(c);
            continue;
        }
        const h1 = hexDigit(input.charCodeAt(i + 1));
        const h2 = hexDigit(input.charCodeAt(i + 2));
        if (h1 < 0 || h2 < 0) {
            bytes.push(c);
            continue;
        }
        bytes.push((h1 << 4) | h2);
        i += 2;
    }
    // UTF-8 decode bytes → string
    return utf8Decode(bytes);
}

function utf8Decode(bytes: number[]): string {
    let out = '';
    let i = 0;
    while (i < bytes.length) {
        const b = bytes[i];
        if (b < 0x80) {
            out += String.fromCharCode(b);
            i++;
        } else if ((b & 0xE0) === 0xC0 && i + 1 < bytes.length) {
            const cp = ((b & 0x1F) << 6) | (bytes[i + 1] & 0x3F);
            out += String.fromCharCode(cp);
            i += 2;
        } else if ((b & 0xF0) === 0xE0 && i + 2 < bytes.length) {
            const cp = ((b & 0x0F) << 12) | ((bytes[i + 1] & 0x3F) << 6) | (bytes[i + 2] & 0x3F);
            out += String.fromCharCode(cp);
            i += 3;
        } else if ((b & 0xF8) === 0xF0 && i + 3 < bytes.length) {
            const cp = ((b & 0x07) << 18) | ((bytes[i + 1] & 0x3F) << 12)
                | ((bytes[i + 2] & 0x3F) << 6) | (bytes[i + 3] & 0x3F);
            // Encode as surrogate pair
            const adjusted = cp - 0x10000;
            out += String.fromCharCode(0xD800 + (adjusted >> 10))
                + String.fromCharCode(0xDC00 + (adjusted & 0x3FF));
            i += 4;
        } else {
            // Invalid byte — pass through
            out += String.fromCharCode(b);
            i++;
        }
    }
    return out;
}

// ─── Scheme / special-scheme helpers ────────────────────────────────

function specialSchemeDefaultPort(scheme: string): number {
    if (scheme === 'http' || scheme === 'ws') return 80;
    if (scheme === 'https' || scheme === 'wss') return 443;
    if (scheme === 'ftp') return 21;
    return -1; // file or unknown: no default port
}

function isSpecialScheme(scheme: string): boolean {
    return scheme === 'http' || scheme === 'https' || scheme === 'ws'
        || scheme === 'wss' || scheme === 'ftp' || scheme === 'file';
}

// ─── URL record ─────────────────────────────────────────────────────

interface URLRecord {
    scheme: string;
    username: string;
    password: string;
    host: string | null;
    port: number | null;
    path: string[];
    opaquePath: string | null; // non-null when URL is an opaque-path URL
    query: string | null;
    fragment: string | null;
    cannotBeABaseURL: boolean;
}

function makeRecord(): URLRecord {
    return {
        scheme: '',
        username: '',
        password: '',
        host: null,
        port: null,
        path: [],
        opaquePath: null,
        query: null,
        fragment: null,
        cannotBeABaseURL: false,
    };
}

function hasAuthority(r: URLRecord): boolean {
    return r.host != null;
}

function isSpecialURL(r: URLRecord): boolean {
    return isSpecialScheme(r.scheme);
}

function includesCredentials(r: URLRecord): boolean {
    return r.username.length > 0 || r.password.length > 0;
}

// ─── Host parser ────────────────────────────────────────────────────

function isForbiddenHostCodePoint(c: number): boolean {
    // C0 controls, space, #, /, :, <, >, ?, @, [, \, ], ^, |
    if (c <= 0x1F) return true;
    return c === CHAR_SPACE || c === CHAR_HASH || c === CHAR_SLASH || c === CHAR_COLON
        || c === CHAR_LT || c === CHAR_GT || c === CHAR_QUESTION || c === CHAR_AT
        || c === CHAR_LBRACKET || c === CHAR_BACKSLASH || c === CHAR_RBRACKET
        || c === CHAR_CARET || c === CHAR_PIPE || c === CHAR_DEL;
}

function isForbiddenDomainCodePoint(c: number): boolean {
    // Forbidden-host + C0 controls + %
    return isForbiddenHostCodePoint(c) || c === CHAR_PERCENT || (c > 0 && c <= 0x1F);
}

function parseIPv4Number(input: string): number {
    // Returns the number, or -1 on failure.
    if (input.length === 0) return -1;
    let radix = 10;
    let trimmed = input;
    if (trimmed.length >= 2 && trimmed.charCodeAt(0) === CHAR_0
        && (trimmed.charCodeAt(1) === 0x58 || trimmed.charCodeAt(1) === 0x78)) {
        // 0X or 0x
        trimmed = trimmed.slice(2);
        radix = 16;
    } else if (trimmed.length >= 2 && trimmed.charCodeAt(0) === CHAR_0) {
        trimmed = trimmed.slice(1);
        radix = 8;
    }
    if (trimmed.length === 0) return 0;
    let result = 0;
    for (let i = 0; i < trimmed.length; i++) {
        const c = trimmed.charCodeAt(i);
        let digit = -1;
        if (radix === 10) digit = isAsciiDigit(c) ? c - CHAR_0 : -1;
        else if (radix === 16) digit = hexDigit(c);
        else if (radix === 8) digit = (c >= CHAR_0 && c <= 0x37) ? c - CHAR_0 : -1;
        if (digit < 0) return -1;
        result = result * radix + digit;
        if (result > 0xFFFFFFFF) return -1;
    }
    return result;
}

function parseIPv4(input: string): string | null {
    // Returns serialized IPv4 address, or null if not parseable as IPv4.
    const parts = input.split('.');
    let effective = parts;
    if (effective.length > 0 && effective[effective.length - 1] === '') {
        effective = effective.slice(0, effective.length - 1);
    }
    if (effective.length === 0 || effective.length > 4) return null;

    const numbers: number[] = [];
    for (let i = 0; i < effective.length; i++) {
        if (effective[i].length === 0) return null;
        const n = parseIPv4Number(effective[i]);
        if (n < 0) return null;
        numbers.push(n);
    }
    // Each except the last must fit in 8 bits.
    for (let i = 0; i < numbers.length - 1; i++) {
        if (numbers[i] > 0xFF) return null;
    }
    // Last part must fit in the remaining bits.
    const last = numbers[numbers.length - 1];
    const maxLast = Math.pow(256, 5 - numbers.length);
    if (last >= maxLast) return null;

    // Assemble 32-bit address.
    let addr = numbers[numbers.length - 1];
    for (let i = 0; i < numbers.length - 1; i++) {
        addr += numbers[i] * Math.pow(256, 3 - i);
    }
    // Serialize as dotted quad.
    const octets: number[] = [];
    for (let i = 0; i < 4; i++) {
        octets.unshift(addr % 256);
        addr = Math.floor(addr / 256);
    }
    return octets[0] + '.' + octets[1] + '.' + octets[2] + '.' + octets[3];
}

function endsInIPv4Number(input: string): boolean {
    // Heuristic: last dot-separated part looks like a number.
    const parts = input.split('.');
    let effective = parts;
    if (effective.length > 0 && effective[effective.length - 1] === '') {
        effective = effective.slice(0, effective.length - 1);
    }
    if (effective.length === 0) return false;
    const last = effective[effective.length - 1];
    if (last.length === 0) return false;
    // All ASCII digits? Then yes.
    let allDigits = true;
    for (let i = 0; i < last.length; i++) {
        if (!isAsciiDigit(last.charCodeAt(i))) { allDigits = false; break; }
    }
    if (allDigits) return true;
    return parseIPv4Number(last) >= 0;
}

function parseIPv6(input: string): string | null {
    // Returns serialized IPv6 (without brackets), or null on failure.
    const pieces: number[] = [0, 0, 0, 0, 0, 0, 0, 0];
    let pieceIndex = 0;
    let compress = -1;
    let pointer = 0;
    const len = input.length;

    if (pointer < len && input.charCodeAt(pointer) === CHAR_COLON) {
        if (pointer + 1 >= len || input.charCodeAt(pointer + 1) !== CHAR_COLON) return null;
        pointer += 2;
        pieceIndex++;
        compress = pieceIndex;
    }

    while (pointer < len) {
        if (pieceIndex === 8) return null;

        if (input.charCodeAt(pointer) === CHAR_COLON) {
            if (compress !== -1) return null;
            pointer++;
            pieceIndex++;
            compress = pieceIndex;
            continue;
        }

        let value = 0;
        let length = 0;
        while (length < 4 && pointer < len && isAsciiHex(input.charCodeAt(pointer))) {
            value = value * 16 + hexDigit(input.charCodeAt(pointer));
            pointer++;
            length++;
        }

        const ch = pointer < len ? input.charCodeAt(pointer) : 0;
        if (ch === CHAR_DOT) {
            // IPv4-in-IPv6. Embed the trailing IPv4 in the last two pieces.
            if (length === 0) return null;
            pointer -= length;
            if (pieceIndex > 6) return null;
            let numbersSeen = 0;
            while (pointer < len) {
                let ipv4Piece = -1;
                if (numbersSeen > 0) {
                    if (input.charCodeAt(pointer) === CHAR_DOT && numbersSeen < 4) pointer++;
                    else return null;
                }
                if (pointer >= len || !isAsciiDigit(input.charCodeAt(pointer))) return null;
                while (pointer < len && isAsciiDigit(input.charCodeAt(pointer))) {
                    const digit = input.charCodeAt(pointer) - CHAR_0;
                    if (ipv4Piece === -1) ipv4Piece = digit;
                    else if (ipv4Piece === 0) return null;
                    else ipv4Piece = ipv4Piece * 10 + digit;
                    if (ipv4Piece > 255) return null;
                    pointer++;
                }
                pieces[pieceIndex] = pieces[pieceIndex] * 256 + ipv4Piece;
                numbersSeen++;
                if (numbersSeen === 2 || numbersSeen === 4) pieceIndex++;
            }
            if (numbersSeen !== 4) return null;
            break;
        }

        if (ch === CHAR_COLON) {
            pointer++;
            if (pointer >= len) return null;
        } else if (pointer < len) {
            return null;
        }

        pieces[pieceIndex] = value;
        pieceIndex++;
    }

    if (compress !== -1) {
        const swaps = pieceIndex - compress;
        pieceIndex = 7;
        while (pieceIndex !== 0 && swaps > 0) {
            const swap = compress + swaps - 1;
            const tmp = pieces[pieceIndex];
            pieces[pieceIndex] = pieces[swap];
            pieces[swap] = tmp;
            pieceIndex--;
            // swaps counter implicit by loop
            break; // Simplified: we only need to shift the compressed range.
        }
        // Proper rearrangement: shift pieces from [compress..pieceIndex-1] to the tail.
        // Recompute cleanly.
        const before = compress;
        const src: number[] = [];
        for (let i = 0; i < before; i++) src.push(pieces[i]);
        const tailCount = swaps;
        const tail: number[] = [];
        for (let i = 0; i < tailCount; i++) tail.push(pieces[before + i]);
        const full: number[] = [];
        for (let i = 0; i < 8; i++) full.push(0);
        for (let i = 0; i < src.length; i++) full[i] = src[i];
        for (let i = 0; i < tail.length; i++) full[8 - tail.length + i] = tail[i];
        for (let i = 0; i < 8; i++) pieces[i] = full[i];
    } else if (pieceIndex !== 8) {
        return null;
    }

    return serializeIPv6(pieces);
}

function serializeIPv6(pieces: number[]): string {
    // Find the longest run of zeros (length >= 2).
    let bestStart = -1;
    let bestLen = 0;
    let curStart = -1;
    let curLen = 0;
    for (let i = 0; i < 8; i++) {
        if (pieces[i] === 0) {
            if (curStart === -1) curStart = i;
            curLen++;
            if (curLen > bestLen) { bestLen = curLen; bestStart = curStart; }
        } else {
            curStart = -1;
            curLen = 0;
        }
    }
    if (bestLen < 2) { bestStart = -1; bestLen = 0; }

    let out = '';
    for (let i = 0; i < 8; i++) {
        if (i === bestStart) {
            out += i === 0 ? '::' : ':';
            i += bestLen - 1;
            continue;
        }
        out += pieces[i].toString(16);
        if (i < 7) out += ':';
    }
    return out;
}

function parseOpaqueHost(input: string): string | null {
    for (let i = 0; i < input.length; i++) {
        const c = input.charCodeAt(i);
        if (isForbiddenHostCodePoint(c) && c !== CHAR_PERCENT) return null;
    }
    return percentEncodeString(input, isC0ControlPercentEncode);
}

function parseHost(input: string, isNotSpecial: boolean): string | null {
    if (input.length > 0 && input.charCodeAt(0) === CHAR_LBRACKET) {
        if (input.charCodeAt(input.length - 1) !== CHAR_RBRACKET) return null;
        const inner = input.slice(1, input.length - 1);
        const v6 = parseIPv6(inner);
        if (v6 == null) return null;
        return '[' + v6 + ']';
    }
    if (isNotSpecial) {
        return parseOpaqueHost(input);
    }
    // Decode, then check forbidden-domain, then try IPv4, else lowercase and validate.
    const decoded = percentDecode(input);
    for (let i = 0; i < decoded.length; i++) {
        if (isForbiddenDomainCodePoint(decoded.charCodeAt(i))) return null;
    }
    // Lowercase ASCII domain.
    let ascii = '';
    for (let i = 0; i < decoded.length; i++) {
        ascii += String.fromCharCode(asciiLower(decoded.charCodeAt(i)));
    }
    if (endsInIPv4Number(ascii)) {
        const v4 = parseIPv4(ascii);
        if (v4 == null) return null;
        return v4;
    }
    return ascii;
}

function serializeHost(host: string | null): string {
    if (host == null) return '';
    return host;
}

// ─── Port validation ────────────────────────────────────────────────

function isDefaultPortForScheme(port: number, scheme: string): boolean {
    return specialSchemeDefaultPort(scheme) === port;
}

// ─── URL parser state machine ───────────────────────────────────────

const STATE_SCHEME_START = 1;
const STATE_SCHEME = 2;
const STATE_NO_SCHEME = 3;
const STATE_SPECIAL_RELATIVE_OR_AUTHORITY = 4;
const STATE_PATH_OR_AUTHORITY = 5;
const STATE_RELATIVE = 6;
const STATE_RELATIVE_SLASH = 7;
const STATE_SPECIAL_AUTHORITY_SLASHES = 8;
const STATE_SPECIAL_AUTHORITY_IGNORE_SLASHES = 9;
const STATE_AUTHORITY = 10;
const STATE_HOST = 11;
const STATE_PORT = 12;
const STATE_FILE = 13;
const STATE_FILE_SLASH = 14;
const STATE_FILE_HOST = 15;
const STATE_PATH_START = 16;
const STATE_PATH = 17;
const STATE_OPAQUE_PATH = 18;
const STATE_QUERY = 19;
const STATE_FRAGMENT = 20;

function isWindowsDriveLetter(s: string): boolean {
    if (s.length !== 2) return false;
    const c0 = s.charCodeAt(0);
    const c1 = s.charCodeAt(1);
    return isAsciiAlpha(c0) && (c1 === CHAR_COLON || c1 === CHAR_PIPE);
}

function isNormalizedWindowsDriveLetter(s: string): boolean {
    return s.length === 2 && isAsciiAlpha(s.charCodeAt(0)) && s.charCodeAt(1) === CHAR_COLON;
}

function startsWithWindowsDriveLetter(s: string): boolean {
    if (s.length < 2) return false;
    if (!isWindowsDriveLetter(s.substring(0, 2))) return false;
    if (s.length === 2) return true;
    const c = s.charCodeAt(2);
    return c === CHAR_SLASH || c === CHAR_BACKSLASH || c === CHAR_QUESTION || c === CHAR_HASH;
}

function shortenPath(url: URLRecord): void {
    if (url.path.length === 0) return;
    if (url.scheme === 'file' && url.path.length === 1 && isNormalizedWindowsDriveLetter(url.path[0])) {
        return;
    }
    url.path.pop();
}

function stripTabsAndNewlines(input: string): string {
    let out = '';
    for (let i = 0; i < input.length; i++) {
        const c = input.charCodeAt(i);
        if (c === CHAR_TAB || c === CHAR_LF || c === CHAR_CR) continue;
        out += input[i];
    }
    return out;
}

function stripLeadingAndTrailingC0Control(input: string): string {
    let start = 0;
    let end = input.length;
    while (start < end) {
        const c = input.charCodeAt(start);
        if (c <= CHAR_SPACE) start++;
        else break;
    }
    while (end > start) {
        const c = input.charCodeAt(end - 1);
        if (c <= CHAR_SPACE) end--;
        else break;
    }
    return input.substring(start, end);
}

function basicUrlParse(input: string, base?: URLRecord | null): URLRecord | null {
    let str = stripLeadingAndTrailingC0Control(input);
    str = stripTabsAndNewlines(str);
    const url = makeRecord();
    const len = str.length;

    let state = STATE_SCHEME_START;
    let buffer = '';
    let atSignSeen = false;
    let insideBrackets = false;
    let passwordTokenSeen = false;
    let pointer = 0;

    while (pointer <= len) {
        const c = pointer < len ? str.charCodeAt(pointer) : -1; // -1 = EOF
        switch (state) {
            case STATE_SCHEME_START:
                if (c >= 0 && isAsciiAlpha(c)) {
                    buffer += String.fromCharCode(asciiLower(c));
                    state = STATE_SCHEME;
                } else {
                    state = STATE_NO_SCHEME;
                    pointer--;
                }
                break;

            case STATE_SCHEME:
                if (c >= 0 && (isAsciiAlphanumeric(c) || c === CHAR_PLUS || c === CHAR_MINUS || c === CHAR_DOT)) {
                    buffer += String.fromCharCode(asciiLower(c));
                } else if (c === CHAR_COLON) {
                    url.scheme = buffer;
                    buffer = '';
                    if (url.scheme === 'file') {
                        state = STATE_FILE;
                    } else if (isSpecialURL(url) && base != null && base.scheme === url.scheme) {
                        state = STATE_SPECIAL_RELATIVE_OR_AUTHORITY;
                    } else if (isSpecialURL(url)) {
                        state = STATE_SPECIAL_AUTHORITY_SLASHES;
                    } else if (pointer + 1 < len && str.charCodeAt(pointer + 1) === CHAR_SLASH) {
                        state = STATE_PATH_OR_AUTHORITY;
                        pointer++;
                    } else {
                        url.opaquePath = '';
                        state = STATE_OPAQUE_PATH;
                    }
                } else {
                    buffer = '';
                    state = STATE_NO_SCHEME;
                    pointer = -1;
                }
                break;

            case STATE_NO_SCHEME:
                if (base == null) return null;
                if (base.opaquePath != null) {
                    if (c !== CHAR_HASH) return null;
                    url.scheme = base.scheme;
                    url.opaquePath = base.opaquePath;
                    url.query = base.query;
                    url.fragment = '';
                    state = STATE_FRAGMENT;
                } else {
                    if (base.scheme !== 'file') {
                        state = STATE_RELATIVE;
                        pointer--;
                    } else {
                        state = STATE_FILE;
                        pointer--;
                    }
                }
                break;

            case STATE_SPECIAL_RELATIVE_OR_AUTHORITY:
                if (c === CHAR_SLASH && pointer + 1 < len && str.charCodeAt(pointer + 1) === CHAR_SLASH) {
                    state = STATE_SPECIAL_AUTHORITY_IGNORE_SLASHES;
                    pointer++;
                } else {
                    state = STATE_RELATIVE;
                    pointer--;
                }
                break;

            case STATE_PATH_OR_AUTHORITY:
                if (c === CHAR_SLASH) state = STATE_AUTHORITY;
                else { state = STATE_PATH; pointer--; }
                break;

            case STATE_RELATIVE:
                url.scheme = base!.scheme;
                if (c === CHAR_SLASH) {
                    state = STATE_RELATIVE_SLASH;
                } else if (isSpecialURL(url) && c === CHAR_BACKSLASH) {
                    state = STATE_RELATIVE_SLASH;
                } else {
                    url.username = base!.username;
                    url.password = base!.password;
                    url.host = base!.host;
                    url.port = base!.port;
                    url.path = base!.path.slice();
                    url.query = base!.query;
                    if (c === CHAR_QUESTION) {
                        url.query = '';
                        state = STATE_QUERY;
                    } else if (c === CHAR_HASH) {
                        url.fragment = '';
                        state = STATE_FRAGMENT;
                    } else if (c !== -1) {
                        url.query = null;
                        if (url.path.length > 0) url.path.pop();
                        state = STATE_PATH;
                        pointer--;
                    }
                }
                break;

            case STATE_RELATIVE_SLASH:
                if (isSpecialURL(url) && (c === CHAR_SLASH || c === CHAR_BACKSLASH)) {
                    state = STATE_SPECIAL_AUTHORITY_IGNORE_SLASHES;
                } else if (c === CHAR_SLASH) {
                    state = STATE_AUTHORITY;
                } else {
                    url.username = base!.username;
                    url.password = base!.password;
                    url.host = base!.host;
                    url.port = base!.port;
                    state = STATE_PATH;
                    pointer--;
                }
                break;

            case STATE_SPECIAL_AUTHORITY_SLASHES:
                if (c === CHAR_SLASH && pointer + 1 < len && str.charCodeAt(pointer + 1) === CHAR_SLASH) {
                    state = STATE_SPECIAL_AUTHORITY_IGNORE_SLASHES;
                    pointer++;
                } else {
                    state = STATE_SPECIAL_AUTHORITY_IGNORE_SLASHES;
                    pointer--;
                }
                break;

            case STATE_SPECIAL_AUTHORITY_IGNORE_SLASHES:
                if (c !== CHAR_SLASH && c !== CHAR_BACKSLASH) {
                    state = STATE_AUTHORITY;
                    pointer--;
                }
                break;

            case STATE_AUTHORITY:
                if (c === CHAR_AT) {
                    if (atSignSeen) buffer = '%40' + buffer;
                    atSignSeen = true;
                    for (let i = 0; i < buffer.length; i++) {
                        const cp = buffer.charCodeAt(i);
                        if (cp === CHAR_COLON && !passwordTokenSeen) {
                            passwordTokenSeen = true;
                            continue;
                        }
                        const encoded = percentEncodeCodePoint(cp, isUserinfoPercentEncode);
                        if (passwordTokenSeen) url.password += encoded;
                        else url.username += encoded;
                    }
                    buffer = '';
                } else if (c === -1 || c === CHAR_SLASH || c === CHAR_QUESTION || c === CHAR_HASH
                    || (isSpecialURL(url) && c === CHAR_BACKSLASH)) {
                    if (atSignSeen && buffer.length === 0) return null;
                    pointer -= buffer.length + 1;
                    buffer = '';
                    state = STATE_HOST;
                } else {
                    buffer += String.fromCharCode(c);
                }
                break;

            case STATE_HOST:
                if (c === CHAR_COLON && !insideBrackets) {
                    if (buffer.length === 0) return null;
                    const host = parseHost(buffer, !isSpecialURL(url));
                    if (host == null) return null;
                    url.host = host;
                    buffer = '';
                    state = STATE_PORT;
                } else if (c === -1 || c === CHAR_SLASH || c === CHAR_QUESTION || c === CHAR_HASH
                    || (isSpecialURL(url) && c === CHAR_BACKSLASH)) {
                    pointer--;
                    if (isSpecialURL(url) && buffer.length === 0) return null;
                    const host = parseHost(buffer, !isSpecialURL(url));
                    if (host == null) return null;
                    url.host = host;
                    buffer = '';
                    state = STATE_PATH_START;
                } else {
                    if (c === CHAR_LBRACKET) insideBrackets = true;
                    else if (c === CHAR_RBRACKET) insideBrackets = false;
                    buffer += String.fromCharCode(c);
                }
                break;

            case STATE_PORT:
                if (c >= 0 && isAsciiDigit(c)) {
                    buffer += String.fromCharCode(c);
                } else if (c === -1 || c === CHAR_SLASH || c === CHAR_QUESTION || c === CHAR_HASH
                    || (isSpecialURL(url) && c === CHAR_BACKSLASH)) {
                    if (buffer.length > 0) {
                        const port = parseInt(buffer, 10);
                        if (port > 0xFFFF) return null;
                        url.port = isDefaultPortForScheme(port, url.scheme) ? null : port;
                        buffer = '';
                    }
                    state = STATE_PATH_START;
                    pointer--;
                } else {
                    return null;
                }
                break;

            case STATE_FILE:
                url.scheme = 'file';
                url.host = '';
                if (c === CHAR_SLASH || c === CHAR_BACKSLASH) {
                    state = STATE_FILE_SLASH;
                } else if (base != null && base.scheme === 'file') {
                    url.host = base.host;
                    url.path = base.path.slice();
                    url.query = base.query;
                    if (c === CHAR_QUESTION) {
                        url.query = '';
                        state = STATE_QUERY;
                    } else if (c === CHAR_HASH) {
                        url.fragment = '';
                        state = STATE_FRAGMENT;
                    } else if (c !== -1) {
                        url.query = null;
                        if (!startsWithWindowsDriveLetter(str.substring(pointer))) {
                            if (url.path.length > 0) url.path.pop();
                        } else {
                            url.path = [];
                        }
                        state = STATE_PATH;
                        pointer--;
                    }
                } else {
                    state = STATE_PATH;
                    pointer--;
                }
                break;

            case STATE_FILE_SLASH:
                if (c === CHAR_SLASH || c === CHAR_BACKSLASH) {
                    state = STATE_FILE_HOST;
                } else {
                    if (base != null && base.scheme === 'file') {
                        url.host = base.host;
                        if (!startsWithWindowsDriveLetter(str.substring(pointer))
                            && base.path.length > 0 && isNormalizedWindowsDriveLetter(base.path[0])) {
                            url.path.push(base.path[0]);
                        }
                    }
                    state = STATE_PATH;
                    pointer--;
                }
                break;

            case STATE_FILE_HOST:
                if (c === -1 || c === CHAR_SLASH || c === CHAR_BACKSLASH
                    || c === CHAR_QUESTION || c === CHAR_HASH) {
                    pointer--;
                    if (isWindowsDriveLetter(buffer)) {
                        state = STATE_PATH;
                    } else if (buffer.length === 0) {
                        url.host = '';
                        state = STATE_PATH_START;
                    } else {
                        let host = parseHost(buffer, !isSpecialURL(url));
                        if (host == null) return null;
                        if (host === 'localhost') host = '';
                        url.host = host;
                        buffer = '';
                        state = STATE_PATH_START;
                    }
                } else {
                    buffer += String.fromCharCode(c);
                }
                break;

            case STATE_PATH_START:
                if (isSpecialURL(url)) {
                    state = STATE_PATH;
                    if (c !== CHAR_SLASH && c !== CHAR_BACKSLASH) pointer--;
                } else if (c === CHAR_QUESTION) {
                    url.query = '';
                    state = STATE_QUERY;
                } else if (c === CHAR_HASH) {
                    url.fragment = '';
                    state = STATE_FRAGMENT;
                } else if (c !== -1) {
                    state = STATE_PATH;
                    if (c !== CHAR_SLASH) pointer--;
                }
                break;

            case STATE_PATH:
                if (c === -1 || c === CHAR_SLASH
                    || (isSpecialURL(url) && c === CHAR_BACKSLASH)
                    || c === CHAR_QUESTION || c === CHAR_HASH) {
                    if (isDoubleDot(buffer)) {
                        shortenPath(url);
                        if (c !== CHAR_SLASH && !(isSpecialURL(url) && c === CHAR_BACKSLASH)) {
                            url.path.push('');
                        }
                    } else if (isSingleDot(buffer)) {
                        if (c !== CHAR_SLASH && !(isSpecialURL(url) && c === CHAR_BACKSLASH)) {
                            url.path.push('');
                        }
                    } else {
                        if (url.scheme === 'file' && url.path.length === 0 && isWindowsDriveLetter(buffer)) {
                            buffer = buffer.charAt(0) + ':';
                        }
                        url.path.push(buffer);
                    }
                    buffer = '';
                    if (c === CHAR_QUESTION) {
                        url.query = '';
                        state = STATE_QUERY;
                    } else if (c === CHAR_HASH) {
                        url.fragment = '';
                        state = STATE_FRAGMENT;
                    }
                } else {
                    buffer += percentEncodeCodePoint(c, isPathPercentEncode);
                }
                break;

            case STATE_OPAQUE_PATH:
                if (c === CHAR_QUESTION) {
                    url.query = '';
                    state = STATE_QUERY;
                } else if (c === CHAR_HASH) {
                    url.fragment = '';
                    state = STATE_FRAGMENT;
                } else if (c !== -1) {
                    url.opaquePath = (url.opaquePath || '') + percentEncodeCodePoint(c, isC0ControlPercentEncode);
                }
                break;

            case STATE_QUERY:
                if (c === -1 || c === CHAR_HASH) {
                    if (c === CHAR_HASH) {
                        url.fragment = '';
                        state = STATE_FRAGMENT;
                    }
                } else {
                    const encodeSet = isSpecialURL(url) ? isSpecialQueryPercentEncode : isQueryPercentEncode;
                    url.query = (url.query || '') + percentEncodeCodePoint(c, encodeSet);
                }
                break;

            case STATE_FRAGMENT:
                if (c !== -1) {
                    url.fragment = (url.fragment || '') + percentEncodeCodePoint(c, isFragmentPercentEncode);
                }
                break;
        }
        pointer++;
    }
    return url;
}

function isSingleDot(s: string): boolean {
    if (s === '.') return true;
    const lower = s.toLowerCase();
    return lower === '%2e';
}

function isDoubleDot(s: string): boolean {
    const lower = s.toLowerCase();
    return lower === '..' || lower === '.%2e' || lower === '%2e.' || lower === '%2e%2e';
}

// ─── URL serializer ─────────────────────────────────────────────────

function urlSerialize(url: URLRecord, excludeFragment?: boolean): string {
    let out = url.scheme + ':';
    if (url.host != null) {
        out += '//';
        if (includesCredentials(url)) {
            out += url.username;
            if (url.password.length > 0) out += ':' + url.password;
            out += '@';
        }
        out += serializeHost(url.host);
        if (url.port != null) out += ':' + url.port;
    } else if (url.host == null && url.scheme === 'file') {
        out += '//';
    }
    if (url.opaquePath != null) {
        out += url.opaquePath;
    } else {
        if (url.host == null && url.path.length > 1 && url.path[0] === '' && url.scheme !== '') {
            // Normalize //path → /.//path per spec; omitted for simplicity.
        }
        for (let i = 0; i < url.path.length; i++) out += '/' + url.path[i];
    }
    if (url.query != null) out += '?' + url.query;
    if (!excludeFragment && url.fragment != null) out += '#' + url.fragment;
    return out;
}

function originOf(url: URLRecord): string {
    if (url.scheme === 'blob') return 'null';
    if (!isSpecialScheme(url.scheme)) return 'null';
    if (url.scheme === 'file') return 'null';
    let out = url.scheme + '://' + serializeHost(url.host);
    if (url.port != null) out += ':' + url.port;
    return out;
}

// ─── URL class ──────────────────────────────────────────────────────

export class URL {
    private _record: URLRecord;
    private _searchParams: URLSearchParams | null;

    constructor(input: string, base?: string) {
        let baseRec: URLRecord | null = null;
        if (base != null) {
            baseRec = basicUrlParse(base);
            if (baseRec == null) throw new TypeError('Invalid base URL: ' + base);
        }
        const rec = basicUrlParse(input, baseRec);
        if (rec == null) throw new TypeError('Invalid URL: ' + input);
        this._record = rec;
        this._searchParams = null;
    }

    get href(): string { return urlSerialize(this._record); }
    set href(value: string) {
        const rec = basicUrlParse(value);
        if (rec == null) throw new TypeError('Invalid URL: ' + value);
        this._record = rec;
        if (this._searchParams != null) {
            (this._searchParams as any)._update(this._record.query);
        }
    }

    get origin(): string { return originOf(this._record); }

    get protocol(): string { return this._record.scheme + ':'; }
    set protocol(value: string) {
        // Parse as scheme-change: feed "<value>X" where X is trailing ':' through scheme state.
        // Simpler: accept if alpha-only followed by optional colon.
        let s = String(value);
        if (s.endsWith(':')) s = s.slice(0, s.length - 1);
        if (s.length === 0 || !isAsciiAlpha(s.charCodeAt(0))) return;
        let lower = '';
        for (let i = 0; i < s.length; i++) {
            const c = s.charCodeAt(i);
            if (!(isAsciiAlphanumeric(c) || c === CHAR_PLUS || c === CHAR_MINUS || c === CHAR_DOT)) return;
            lower += String.fromCharCode(asciiLower(c));
        }
        // Don't allow changing between special/non-special.
        if (isSpecialScheme(this._record.scheme) !== isSpecialScheme(lower)) return;
        this._record.scheme = lower;
    }

    get username(): string { return this._record.username; }
    set username(value: string) {
        if (this._record.host == null || this._record.host === '' || this._record.scheme === 'file') return;
        this._record.username = percentEncodeString(String(value), isUserinfoPercentEncode);
    }

    get password(): string { return this._record.password; }
    set password(value: string) {
        if (this._record.host == null || this._record.host === '' || this._record.scheme === 'file') return;
        this._record.password = percentEncodeString(String(value), isUserinfoPercentEncode);
    }

    get host(): string {
        if (this._record.host == null) return '';
        if (this._record.port == null) return serializeHost(this._record.host);
        return serializeHost(this._record.host) + ':' + this._record.port;
    }
    set host(value: string) {
        if (this._record.opaquePath != null) return;
        // Quick impl: re-parse with a synthetic URL that replaces host.
        const newRec = basicUrlParse(this._record.scheme + '://' + String(value)
            + (this._record.path.length > 0 ? '/' + this._record.path.join('/') : ''));
        if (newRec != null && newRec.host != null) {
            this._record.host = newRec.host;
            this._record.port = newRec.port;
        }
    }

    get hostname(): string { return this._record.host == null ? '' : serializeHost(this._record.host); }
    set hostname(value: string) {
        if (this._record.opaquePath != null) return;
        const host = parseHost(String(value), !isSpecialURL(this._record));
        if (host != null) this._record.host = host;
    }

    get port(): string { return this._record.port == null ? '' : String(this._record.port); }
    set port(value: string) {
        if (this._record.host == null || this._record.host === '' || this._record.scheme === 'file') return;
        const s = String(value);
        if (s.length === 0) { this._record.port = null; return; }
        let n = 0;
        for (let i = 0; i < s.length; i++) {
            if (!isAsciiDigit(s.charCodeAt(i))) return;
            n = n * 10 + (s.charCodeAt(i) - CHAR_0);
            if (n > 0xFFFF) return;
        }
        this._record.port = isDefaultPortForScheme(n, this._record.scheme) ? null : n;
    }

    get pathname(): string {
        if (this._record.opaquePath != null) return this._record.opaquePath;
        if (this._record.path.length === 0) return '';
        let out = '';
        for (let i = 0; i < this._record.path.length; i++) out += '/' + this._record.path[i];
        return out;
    }
    set pathname(value: string) {
        if (this._record.opaquePath != null) return;
        // Simpler: parse value into a fresh path.
        this._record.path = [];
        const str = stripTabsAndNewlines(String(value));
        const fakeRec = makeRecord();
        fakeRec.scheme = this._record.scheme;
        // Feed path through state machine starting at PATH_START.
        // Cheap implementation: split on / and push each segment, handling . and .. as dots.
        // More permissive than spec but adequate for tests.
        if (str.length === 0) return;
        let segs: string[] = [];
        if (str.charCodeAt(0) === CHAR_SLASH) segs = str.substring(1).split('/');
        else segs = str.split('/');
        for (let i = 0; i < segs.length; i++) {
            const s = segs[i];
            if (isSingleDot(s)) continue;
            if (isDoubleDot(s)) { if (this._record.path.length > 0) this._record.path.pop(); continue; }
            this._record.path.push(percentEncodeString(s, isPathPercentEncode));
        }
    }

    get search(): string {
        if (this._record.query == null || this._record.query === '') return '';
        return '?' + this._record.query;
    }
    set search(value: string) {
        let s = String(value);
        if (s.length === 0) {
            this._record.query = null;
            if (this._searchParams != null) (this._searchParams as any)._update(null);
            return;
        }
        if (s.charCodeAt(0) === CHAR_QUESTION) s = s.substring(1);
        const encodeSet = isSpecialURL(this._record) ? isSpecialQueryPercentEncode : isQueryPercentEncode;
        this._record.query = percentEncodeString(stripTabsAndNewlines(s), encodeSet);
        if (this._searchParams != null) (this._searchParams as any)._update(this._record.query);
    }

    get searchParams(): URLSearchParams {
        if (this._searchParams == null) {
            const params = new URLSearchParams(this._record.query || '');
            this._searchParams = params;
            (params as any)._owner = this;
        }
        return this._searchParams as any;
    }

    get hash(): string {
        if (this._record.fragment == null || this._record.fragment === '') return '';
        return '#' + this._record.fragment;
    }
    set hash(value: string) {
        let s = String(value);
        if (s.length === 0) { this._record.fragment = null; return; }
        if (s.charCodeAt(0) === CHAR_HASH) s = s.substring(1);
        this._record.fragment = percentEncodeString(stripTabsAndNewlines(s), isFragmentPercentEncode);
    }

    toString(): string { return urlSerialize(this._record); }
    toJSON(): string { return urlSerialize(this._record); }
}

// ─── URLSearchParams ────────────────────────────────────────────────

export class URLSearchParams {
    // Internal representation: list of [key, value] pairs. Matches the
    // WHATWG spec's "list of name-value pairs" more literally than parallel
    // arrays did, and exercises nested-array push on class fields — a path
    // that had a codegen gap earlier in SharpTS's development.
    private _pairs: string[][];
    private _owner: URL | null;

    constructor(init?: any) {
        this._pairs = [];
        this._owner = null;
        if (init == null) return;
        if (init instanceof URLSearchParams) {
            const src = init as any;
            for (let i = 0; i < src._pairs.length; i++) {
                this._pairs.push([src._pairs[i][0], src._pairs[i][1]]);
            }
            return;
        }
        if (typeof init === 'string') {
            let s = init;
            if (s.length > 0 && s.charCodeAt(0) === CHAR_QUESTION) s = s.substring(1);
            this._parseFromString(s);
            return;
        }
        if (Array.isArray(init)) {
            for (let i = 0; i < init.length; i++) {
                const pair = init[i];
                if (!Array.isArray(pair) || pair.length !== 2) {
                    throw new TypeError('URLSearchParams: each sequence item must be a 2-tuple');
                }
                this._pairs.push([String(pair[0]), String(pair[1])]);
            }
            return;
        }
        if (typeof init === 'object') {
            const keys = Object.keys(init);
            for (const k of keys) {
                this._pairs.push([k, String(init[k])]);
            }
            return;
        }
        this._parseFromString(String(init));
    }

    private _parseFromString(s: string): void {
        if (s.length === 0) return;
        const pairs = s.split('&');
        for (let i = 0; i < pairs.length; i++) {
            const p = pairs[i];
            if (p.length === 0) continue;
            const eq = p.indexOf('=');
            let name: string;
            let value: string;
            if (eq < 0) { name = p; value = ''; }
            else { name = p.substring(0, eq); value = p.substring(eq + 1); }
            name = percentDecode(name.split('+').join(' '));
            value = percentDecode(value.split('+').join(' '));
            this._pairs.push([name, value]);
        }
    }

    private _update(query: string | null): void {
        this._pairs = [];
        if (query != null && query.length > 0) this._parseFromString(query);
    }

    private _notifyOwner(): void {
        if (this._owner == null) return;
        const serialized = this.toString();
        (this._owner as any)._record.query = serialized.length === 0 ? null : serialized;
    }

    get size(): number { return this._pairs.length; }

    append(name: string, value: string): void {
        this._pairs.push([String(name), String(value)]);
        this._notifyOwner();
    }

    get(name: string): string | null {
        return this._getFirst(String(name));
    }

    set(name: string, value: string): void {
        this._setKey(String(name), String(value));
    }

    delete(name: string): void {
        this._deleteKey(String(name));
    }

    private _deleteKey(key: string): void {
        const next: string[][] = [];
        for (let i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] !== key) next.push(this._pairs[i]);
        }
        this._pairs = next;
        this._notifyOwner();
    }

    private _getFirst(key: string): string | null {
        for (let i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] === key) return this._pairs[i][1];
        }
        return null;
    }

    getAll(name: string): string[] {
        const key = String(name);
        const out: string[] = [];
        for (let i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] === key) out.push(this._pairs[i][1]);
        }
        return out;
    }

    has(name: string): boolean {
        const key = String(name);
        for (let i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] === key) return true;
        }
        return false;
    }

    private _setKey(key: string, val: string): void {
        let found = false;
        const next: string[][] = [];
        for (let i = 0; i < this._pairs.length; i++) {
            if (this._pairs[i][0] === key) {
                if (!found) { next.push([key, val]); found = true; }
            } else {
                next.push(this._pairs[i]);
            }
        }
        if (!found) next.push([key, val]);
        this._pairs = next;
        this._notifyOwner();
    }

    sort(): void {
        // Stable sort by key via an index array (Array.sort is stable).
        // The comparator captures `this` lexically from the enclosing method.
        const n = this._pairs.length;
        const indices: number[] = [];
        for (let i = 0; i < n; i++) indices.push(i);
        indices.sort((a: number, b: number): number => {
            if (this._pairs[a][0] < this._pairs[b][0]) return -1;
            if (this._pairs[a][0] > this._pairs[b][0]) return 1;
            return 0;
        });
        const next: string[][] = [];
        for (let i = 0; i < n; i++) next.push(this._pairs[indices[i]]);
        this._pairs = next;
        this._notifyOwner();
    }

    forEach(callback: Function, thisArg?: any): void {
        for (let i = 0; i < this._pairs.length; i++) {
            callback.call(thisArg, this._pairs[i][1], this._pairs[i][0], this);
        }
    }

    keys(): string[] {
        const out: string[] = [];
        for (let i = 0; i < this._pairs.length; i++) out.push(this._pairs[i][0]);
        return out;
    }

    values(): string[] {
        const out: string[] = [];
        for (let i = 0; i < this._pairs.length; i++) out.push(this._pairs[i][1]);
        return out;
    }

    entries(): string[][] {
        const out: string[][] = [];
        for (let i = 0; i < this._pairs.length; i++) {
            out.push([this._pairs[i][0], this._pairs[i][1]]);
        }
        return out;
    }

    toString(): string {
        let out = '';
        for (let i = 0; i < this._pairs.length; i++) {
            if (i > 0) out += '&';
            out += percentEncodeString(this._pairs[i][0], isFormUrlencodedPercentEncode).split('%20').join('+');
            out += '=';
            out += percentEncodeString(this._pairs[i][1], isFormUrlencodedPercentEncode).split('%20').join('+');
        }
        return out;
    }
}

// ─── Legacy Node url.parse / format / resolve ───────────────────────

/**
 * Parsed-URL shape returned by legacy `url.parse`. All fields null when absent.
 */
export interface LegacyUrlObject {
    protocol: string | null;
    slashes: boolean | null;
    auth: string | null;
    host: string | null;
    port: string | null;
    hostname: string | null;
    hash: string | null;
    search: string | null;
    query: string | null;
    pathname: string | null;
    path: string | null;
    href: string;
}

/**
 * Legacy `url.parse(urlString)` — returns an object with pre-WHATWG property shape.
 * Deprecated in Node but retained for backwards compatibility.
 */
export function parse(urlString: string, parseQueryString?: boolean): LegacyUrlObject {
    const rec = basicUrlParse(String(urlString), null);
    const result: LegacyUrlObject = {
        protocol: null,
        slashes: null,
        auth: null,
        host: null,
        port: null,
        hostname: null,
        hash: null,
        search: null,
        query: null,
        pathname: null,
        path: null,
        href: String(urlString),
    };
    if (rec == null) {
        // basicUrlParse returns null for schemeless inputs with no base (e.g.
        // a request-target like '/api/echo?k=v'). Node's legacy parse() still
        // splits on '#' and '?' in that case, so do the same by hand. Split
        // '#' first so a '?' inside a fragment isn't treated as a query.
        let s = String(urlString);
        const hashIdx = s.indexOf('#');
        if (hashIdx >= 0) {
            result.hash = s.substring(hashIdx);
            s = s.substring(0, hashIdx);
        }
        const queryIdx = s.indexOf('?');
        if (queryIdx >= 0) {
            // Stash the substring in a local — the type checker doesn't carry the
            // narrowed `string` from `result.search = ...` back through the next
            // `result.search.substring(1)` read when the RHS comes through `any`
            // (which `String(...).substring(...)` is, here). Reading it from the
            // local sidesteps the property-narrowing path entirely.
            const querySegment = s.substring(queryIdx);
            result.search = querySegment;
            result.query = querySegment.substring(1);
            s = s.substring(0, queryIdx);
        }
        result.pathname = s;
        result.path = (result.pathname ?? '') + (result.search ?? '');
        return result;
    }
    result.protocol = rec.scheme + ':';
    result.slashes = rec.host != null;
    if (rec.username.length > 0 || rec.password.length > 0) {
        result.auth = rec.username + (rec.password.length > 0 ? ':' + rec.password : '');
    }
    if (rec.host != null) {
        result.hostname = serializeHost(rec.host);
        result.host = rec.port != null ? result.hostname + ':' + rec.port : result.hostname;
        result.port = rec.port != null ? String(rec.port) : null;
    }
    if (rec.fragment != null) result.hash = '#' + rec.fragment;
    if (rec.query != null) {
        result.search = '?' + rec.query;
        result.query = rec.query;
    }
    if (rec.opaquePath != null) {
        result.pathname = rec.opaquePath;
    } else {
        const pathArr: string[] = rec.path;
        if (pathArr.length > 0) {
            let joined = '';
            for (let i = 0; i < pathArr.length; i++) joined += '/' + pathArr[i];
            result.pathname = joined;
        } else {
            result.pathname = '/';
        }
    }
    result.path = (result.pathname || '') + (result.search || '');
    result.href = urlSerialize(rec);
    return result;
}

/**
 * Legacy `url.format(urlObject)` — inverse of `parse` for the old shape.
 * Accepts either a WHATWG URL instance or a plain object.
 */
export function format(urlObject: any): string {
    if (urlObject instanceof URL) return urlObject.toString();
    if (typeof urlObject === 'string') return urlObject;
    const o: any = urlObject || {};
    let protocol = o.protocol != null ? String(o.protocol) : '';
    if (protocol.length > 0 && !protocol.endsWith(':')) protocol += ':';
    const hostname = o.hostname != null ? String(o.hostname) : (o.host != null ? String(o.host) : '');
    const port = o.port != null ? String(o.port) : '';
    let host = '';
    if (hostname.length > 0) {
        host = hostname;
        if (port.length > 0 && hostname.indexOf(':') < 0) host = hostname + ':' + port;
    } else if (o.host != null) {
        host = String(o.host);
    }
    const auth = o.auth != null ? String(o.auth) + '@' : '';
    let pathname = o.pathname != null ? String(o.pathname) : '';
    if (pathname.length > 0 && pathname.charCodeAt(0) !== CHAR_SLASH && host.length > 0) {
        pathname = '/' + pathname;
    }
    let search = '';
    if (o.search != null) {
        const s = String(o.search);
        search = s.length > 0 && s.charCodeAt(0) !== CHAR_QUESTION ? '?' + s : s;
    } else if (o.query != null) {
        if (typeof o.query === 'string') search = '?' + o.query;
        else search = '?' + new URLSearchParams(o.query).toString();
    }
    let hash = '';
    if (o.hash != null) {
        const h = String(o.hash);
        hash = h.length > 0 && h.charCodeAt(0) !== CHAR_HASH ? '#' + h : h;
    }
    const slashes = o.slashes !== false && (protocol.length > 0 || host.length > 0);
    return protocol + (slashes && host.length > 0 ? '//' : '') + auth + host + pathname + search + hash;
}

/**
 * Legacy `url.resolve(from, to)` — resolves `to` relative to `from`.
 */
export function resolve(from: string, to: string): string {
    const baseRec = basicUrlParse(String(from));
    if (baseRec == null) {
        if (to.length > 0 && to.charCodeAt(0) === CHAR_SLASH) return to;
        return String(from).replace(/\/+$/, '') + '/' + String(to);
    }
    const rec = basicUrlParse(String(to), baseRec);
    if (rec == null) return String(to);
    return urlSerialize(rec);
}

// ─── fileURLToPath / pathToFileURL ──────────────────────────────────

/** Convert a file:// URL to a platform path string. */
export function fileURLToPath(url: URL | string): string {
    const u = typeof url === 'string' ? new URL(url) : url;
    if (u.protocol !== 'file:') throw new TypeError('The URL must be of scheme file');
    const path = u.pathname;
    return percentDecode(path);
}

/** Convert a platform path string to a file:// URL. */
export function pathToFileURL(path: string): URL {
    let p = String(path);
    // Basic POSIX handling; Windows drive-letter handling is minimal.
    if (p.length > 0 && p.charCodeAt(0) !== CHAR_SLASH) {
        // Treat as relative — prefix cwd would be proper Node behavior; we keep relative here.
        p = '/' + p;
    }
    return new URL('file://' + percentEncodeString(p, isPathPercentEncode));
}

export default {
    URL, URLSearchParams,
    parse, format, resolve,
    fileURLToPath, pathToFileURL,
};
