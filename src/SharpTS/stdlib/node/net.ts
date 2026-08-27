// Node.js 'net' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0.
//
// Socket and Server remain native stateful primitives. This facade owns the
// public argument normalization, IP utilities, SocketAddress value object,
// BlockList validation/rule display, and compatibility-only defaults.

import {
    createServer as __createServer,
    createConnection as __createConnection,
    createSocket as __createSocket,
    createBlockList as __createBlockList,
} from 'primitive:net';

interface ParsedAddress {
    family: number;
    bytes: number[];
    address: string;
    mappedIPv4?: number[];
}

interface BlockRule {
    ipv6: boolean;
    start: number[];
    end: number[];
    display: string;
}

function __error(code: string, message: string): any {
    const error: any = new Error(message);
    error.code = code;
    return error;
}

function __typeError(code: string, message: string): any {
    // Error carries arbitrary Node-style fields in both execution modes. Keep
    // the observable error name/code while the emitted TypeError representation
    // remains intentionally fixed-layout.
    const error: any = new Error(message);
    error.name = 'TypeError';
    error.code = code;
    return error;
}

function __rangeError(code: string, message: string): any {
    const error: any = new Error(message);
    error.name = 'RangeError';
    error.code = code;
    return error;
}

function __parseIPv4(input: string): ParsedAddress | undefined {
    const parts = input.split('.');
    if (parts.length !== 4) return undefined;

    const bytes: number[] = [];
    for (let i = 0; i < 4; i++) {
        const part = parts[i];
        if (part.length === 0) return undefined;
        if (part.length > 1 && part[0] === '0') return undefined;
        for (let j = 0; j < part.length; j++) {
            const code = part.charCodeAt(j);
            if (code < 48 || code > 57) return undefined;
        }
        const value = parseInt(part, 10);
        if (value < 0 || value > 255) return undefined;
        bytes.push(value);
    }

    return { family: 4, bytes, address: bytes.join('.') };
}

function __isHexGroup(group: string): boolean {
    if (group.length < 1 || group.length > 4) return false;
    for (let i = 0; i < group.length; i++) {
        const code = group.charCodeAt(i);
        const digit = code >= 48 && code <= 57;
        const lower = code >= 97 && code <= 102;
        const upper = code >= 65 && code <= 70;
        if (!digit && !lower && !upper) return false;
    }
    return true;
}

function __hex(value: number): string {
    return value.toString(16);
}

function __canonicalIPv6(bytes: number[]): string {
    let mapped = true;
    for (let i = 0; i < 10; i++) {
        if (bytes[i] !== 0) mapped = false;
    }
    if (mapped && bytes[10] === 255 && bytes[11] === 255) {
        return '::ffff:' + bytes[12] + '.' + bytes[13] + '.' + bytes[14] + '.' + bytes[15];
    }

    const groups: number[] = [];
    for (let i = 0; i < 16; i += 2) groups.push(bytes[i] * 256 + bytes[i + 1]);

    let bestStart = -1;
    let bestLength = 0;
    let runStart = -1;
    for (let i = 0; i <= 8; i++) {
        if (i < 8 && groups[i] === 0) {
            if (runStart < 0) runStart = i;
        } else if (runStart >= 0) {
            const length = i - runStart;
            if (length > bestLength && length >= 2) {
                bestStart = runStart;
                bestLength = length;
            }
            runStart = -1;
        }
    }

    let result = '';
    let index = 0;
    while (index < 8) {
        if (index === bestStart) {
            result += '::';
            index += bestLength;
            continue;
        }
        if (result.length > 0 && result[result.length - 1] !== ':') result += ':';
        result += __hex(groups[index]);
        index++;
    }
    return result.length === 0 ? '::' : result;
}

function __parseIPv6(input: string): ParsedAddress | undefined {
    let address = input;
    const zoneIndex = address.indexOf('%');
    if (zoneIndex >= 0) {
        if (zoneIndex === address.length - 1) return undefined;
        address = address.substring(0, zoneIndex);
    }
    if (address.length === 0) return undefined;

    if (address.indexOf('.') >= 0) {
        const colon = address.lastIndexOf(':');
        if (colon < 0) return undefined;
        const v4 = __parseIPv4(address.substring(colon + 1));
        if (v4 === undefined) return undefined;
        const high = v4.bytes[0] * 256 + v4.bytes[1];
        const low = v4.bytes[2] * 256 + v4.bytes[3];
        address = address.substring(0, colon + 1) + __hex(high) + ':' + __hex(low);
    }

    const doubleIndex = address.indexOf('::');
    if (doubleIndex >= 0 && address.indexOf('::', doubleIndex + 2) >= 0) return undefined;

    let left: string[] = [];
    let right: string[] = [];
    if (doubleIndex >= 0) {
        const leftText = address.substring(0, doubleIndex);
        const rightText = address.substring(doubleIndex + 2);
        if (leftText.length > 0) left = leftText.split(':');
        if (rightText.length > 0) right = rightText.split(':');
        if (left.length + right.length >= 8) return undefined;
    } else {
        left = address.split(':');
        if (left.length !== 8) return undefined;
    }

    const groups: number[] = [];
    for (let i = 0; i < left.length; i++) {
        if (!__isHexGroup(left[i])) return undefined;
        groups.push(parseInt(left[i], 16));
    }
    if (doubleIndex >= 0) {
        const missing = 8 - left.length - right.length;
        for (let i = 0; i < missing; i++) groups.push(0);
    }
    for (let i = 0; i < right.length; i++) {
        if (!__isHexGroup(right[i])) return undefined;
        groups.push(parseInt(right[i], 16));
    }
    if (groups.length !== 8) return undefined;

    const bytes: number[] = [];
    for (let i = 0; i < 8; i++) {
        bytes.push((groups[i] >> 8) & 255);
        bytes.push(groups[i] & 255);
    }

    let mappedIPv4: number[] | undefined = undefined;
    let mapped = bytes[10] === 255 && bytes[11] === 255;
    for (let i = 0; i < 10; i++) {
        if (bytes[i] !== 0) mapped = false;
    }
    if (mapped) mappedIPv4 = [bytes[12], bytes[13], bytes[14], bytes[15]];

    return { family: 6, bytes, address: __canonicalIPv6(bytes), mappedIPv4 };
}

function __parseAddress(input: any): ParsedAddress | undefined {
    if (typeof input !== 'string') return undefined;
    const v4 = __parseIPv4(input);
    if (v4 !== undefined) return v4;
    return __parseIPv6(input);
}

function __compareBytes(left: number[], right: number[]): number {
    for (let i = 0; i < left.length; i++) {
        if (left[i] < right[i]) return -1;
        if (left[i] > right[i]) return 1;
    }
    return 0;
}

function __subnetBounds(bytes: number[], prefix: number): any {
    const start: number[] = [];
    const end: number[] = [];
    for (let i = 0; i < bytes.length; i++) {
        const bits = prefix - i * 8;
        const mask = bits >= 8 ? 255 : bits <= 0 ? 0 : (255 << (8 - bits)) & 255;
        start.push(bytes[i] & mask);
        end.push(bytes[i] | (~mask & 255));
    }
    return { start, end };
}

function __familyName(ipv6: boolean): string {
    return ipv6 ? 'IPv6' : 'IPv4';
}

function __familyOption(family: any): boolean {
    if (family === undefined) return false;
    if (typeof family !== 'string') {
        throw __typeError('ERR_INVALID_ARG_VALUE', "The argument 'family' is invalid. Received '" + family + "'");
    }
    const normalized = family.toLowerCase();
    if (normalized === 'ipv4') return false;
    if (normalized === 'ipv6') return true;
    throw __typeError('ERR_INVALID_ARG_VALUE', "The argument 'family' is invalid. Received '" + family + "'");
}

function __resolveRuleAddress(value: any, family?: any): any {
    let address: string;
    let ipv6: boolean;
    if (value instanceof SocketAddress) {
        address = value.address;
        ipv6 = value.family === 'ipv6';
    } else {
        if (typeof value !== 'string') {
            throw __error('ERR_INVALID_ARG_TYPE', "The 'address' argument must be of type string or SocketAddress");
        }
        address = value;
        ipv6 = __familyOption(family);
    }

    const parsed = __parseAddress(address);
    if (parsed === undefined) return undefined;
    if (ipv6) {
        if (parsed.family !== 6) return undefined;
        return { address: parsed.address, bytes: parsed.bytes, ipv6: true, mappedIPv4: parsed.mappedIPv4 };
    }
    if (parsed.family === 4) return { address: parsed.address, bytes: parsed.bytes, ipv6: false };
    if (parsed.mappedIPv4 !== undefined) {
        return { address: parsed.mappedIPv4.join('.'), bytes: parsed.mappedIPv4, ipv6: false };
    }
    return undefined;
}

/** Returns 4 for IPv4, 6 for IPv6, and 0 for invalid input. */
export function isIP(input: any): number {
    const parsed = __parseAddress(input);
    return parsed === undefined ? 0 : parsed.family;
}

export function isIPv4(input: any): boolean {
    const parsed = __parseAddress(input);
    return parsed !== undefined && parsed.family === 4;
}

export function isIPv6(input: any): boolean {
    const parsed = __parseAddress(input);
    return parsed !== undefined && parsed.family === 6;
}

/** Immutable Node-shaped IP endpoint value. */
export class SocketAddress {
    readonly address: string;
    readonly family: string;
    readonly port: number;
    readonly flowlabel: number;

    constructor(options?: any) {
        const opts: any = options === undefined ? {} : options;
        if (opts === null || typeof opts !== 'object') {
            throw __typeError('ERR_INVALID_ARG_TYPE', "The 'options' argument must be of type object");
        }

        if (opts.family !== undefined && typeof opts.family !== 'string') {
            throw __typeError('ERR_INVALID_ARG_VALUE', "The property 'options.family' is invalid. Received " + opts.family);
        }
        const family = opts.family === undefined ? 'ipv4' : opts.family.toLowerCase();
        if (family !== 'ipv4' && family !== 'ipv6') {
            throw __typeError('ERR_INVALID_ARG_VALUE', "The property 'options.family' is invalid. Received '" + opts.family + "'");
        }

        const defaultAddress = family === 'ipv6' ? '::' : '127.0.0.1';
        const rawAddress = opts.address === undefined ? defaultAddress : opts.address;
        if (typeof rawAddress !== 'string') {
            throw __typeError('ERR_INVALID_ARG_TYPE', "The 'options.address' property must be of type string");
        }
        const parsed = __parseAddress(rawAddress);
        if (parsed === undefined || (family === 'ipv4' && parsed.family !== 4) || (family === 'ipv6' && parsed.family !== 6)) {
            throw __error('ERR_INVALID_ADDRESS', 'Invalid socket address');
        }

        let port = opts.port === undefined ? 0 : Number(opts.port);
        if (!Number.isFinite(port) || Math.floor(port) !== port || port < 0 || port >= 65536) {
            throw __rangeError('ERR_SOCKET_BAD_PORT', 'options.port should be >= 0 and < 65536');
        }

        let flowlabel = 0;
        if (family === 'ipv6' && opts.flowlabel !== undefined) {
            if (typeof opts.flowlabel !== 'number') {
                throw __typeError('ERR_INVALID_ARG_TYPE', "The 'options.flowlabel' property must be of type number");
            }
            flowlabel = opts.flowlabel;
            if (!Number.isFinite(flowlabel) || Math.floor(flowlabel) !== flowlabel || flowlabel < 0 || flowlabel > 1048575) {
                throw __rangeError('ERR_OUT_OF_RANGE', "The value of 'options.flowlabel' is out of range");
            }
        }

        this.address = parsed.address;
        this.family = family;
        this.port = port;
        this.flowlabel = flowlabel;
    }

    toJSON(): any {
        return { address: this.address, port: this.port, family: this.family, flowlabel: this.flowlabel };
    }
}

/** Node-compatible blocked-address rule set. */
export class BlockList {
    private _rules: BlockRule[] = [];
    private _handle: any;

    constructor() {
        this._handle = __createBlockList();
    }

    /** Internal facade seam used only when constructing a native Server. */
    __nativeHandle(): any { return this._handle; }

    get rules(): string[] {
        const result: string[] = [];
        for (let i = 0; i < this._rules.length; i++) result.push(this._rules[i].display);
        return result;
    }

    addAddress(address: any, family?: string): void {
        const resolved = __resolveRuleAddress(address, family);
        if (resolved === undefined) {
            throw __error('ERR_INVALID_ADDRESS', 'Invalid socket address');
        }
        this._handle.addAddress(resolved.address, resolved.ipv6 ? 'ipv6' : 'ipv4');
        this._rules.unshift({
            ipv6: resolved.ipv6,
            start: resolved.bytes,
            end: resolved.bytes,
            display: 'Address: ' + __familyName(resolved.ipv6) + ' ' + resolved.address,
        });
    }

    addRange(start: any, end: any, family?: string): void {
        const first = __resolveRuleAddress(start, family);
        const last = __resolveRuleAddress(end, family);
        if (first === undefined) {
            throw __error('ERR_INVALID_ADDRESS', 'Invalid socket address');
        }
        if (last === undefined || first.ipv6 !== last.ipv6) {
            throw __error('ERR_INVALID_ADDRESS', 'Invalid socket address');
        }
        if (__compareBytes(first.bytes, last.bytes) > 0) {
            throw __error('ERR_INVALID_ARG_VALUE', "The argument 'start' must come before 'end'");
        }
        this._handle.addRange(first.address, last.address, first.ipv6 ? 'ipv6' : 'ipv4');
        this._rules.unshift({
            ipv6: first.ipv6,
            start: first.bytes,
            end: last.bytes,
            display: 'Range: ' + __familyName(first.ipv6) + ' ' + first.address + '-' + last.address,
        });
    }

    addSubnet(network: any, prefix: number, family?: string): void {
        const resolved = __resolveRuleAddress(network, family);
        if (resolved === undefined) {
            throw __error('ERR_INVALID_ADDRESS', 'Invalid socket address');
        }
        if (typeof prefix !== 'number') {
            throw __typeError('ERR_INVALID_ARG_TYPE', "The 'prefix' argument must be of type number");
        }
        const max = resolved.ipv6 ? 128 : 32;
        if (Math.floor(prefix) !== prefix || prefix < 0 || prefix > max) {
            throw __rangeError('ERR_OUT_OF_RANGE', "The value of 'prefix' is out of range");
        }
        const bounds = __subnetBounds(resolved.bytes, prefix);
        this._handle.addSubnet(resolved.address, prefix, resolved.ipv6 ? 'ipv6' : 'ipv4');
        this._rules.unshift({
            ipv6: resolved.ipv6,
            start: bounds.start,
            end: bounds.end,
            display: 'Subnet: ' + __familyName(resolved.ipv6) + ' ' + resolved.address + '/' + prefix,
        });
    }

    check(address: any, family?: string): boolean {
        let resolved: any;
        try {
            resolved = __resolveRuleAddress(address, family);
        } catch (_error) {
            return false;
        }
        if (resolved === undefined) return false;

        for (let i = 0; i < this._rules.length; i++) {
            const rule = this._rules[i];
            if (rule.ipv6 === resolved.ipv6
                && __compareBytes(rule.start, resolved.bytes) <= 0
                && __compareBytes(resolved.bytes, rule.end) <= 0) return true;
        }

        if (resolved.ipv6 && resolved.mappedIPv4 !== undefined) {
            for (let i = 0; i < this._rules.length; i++) {
                const rule = this._rules[i];
                if (!rule.ipv6
                    && __compareBytes(rule.start, resolved.mappedIPv4) <= 0
                    && __compareBytes(resolved.mappedIPv4, rule.end) <= 0) return true;
            }
        }
        return false;
    }
}

function __serverOptions(options: any): any {
    if (options === undefined || options === null) return options;
    if (typeof options !== 'object') {
        throw __typeError('ERR_INVALID_ARG_TYPE', "The 'options' argument must be of type object");
    }
    const blockList = options.blockList instanceof BlockList
        ? options.blockList.__nativeHandle()
        : options.blockList;
    return {
        highWaterMark: options.highWaterMark,
        blockList,
        allowHalfOpen: options.allowHalfOpen,
    };
}

export function createServer(options?: any, connectionListener?: any): any {
    if (typeof options === 'function') return __createServer(options);
    if (options === undefined) {
        if (connectionListener === undefined) return __createServer();
        return __createServer(connectionListener);
    }
    const nativeOptions = __serverOptions(options);
    if (connectionListener === undefined) return __createServer(nativeOptions);
    return __createServer(nativeOptions, connectionListener);
}

function __connect(options: any, hostOrListener?: any, connectionListener?: any): any {
    if (connectionListener !== undefined) return __createConnection(options, hostOrListener, connectionListener);
    if (hostOrListener !== undefined) return __createConnection(options, hostOrListener);
    return __createConnection(options);
}

// Both names must expose the exact same function object (Node guarantees
// `connect === createConnection`). Keeping both as value exports also preserves
// that identity in standalone compiled modules.
export const createConnection: any = __connect;
export const connect: any = createConnection;

// Node permits Server and Socket to be called with or without `new`. JavaScript
// constructor functions may return the native stateful object explicitly.
export const Server: any = function(options?: any, connectionListener?: any): any {
    return createServer(options, connectionListener);
};

export const Socket: any = function(options?: any): any {
    if (options === undefined) return __createSocket();
    return __createSocket(options);
};

let __defaultAutoSelectFamily = true;
let __defaultAutoSelectFamilyAttemptTimeout = 250;

export function getDefaultAutoSelectFamily(): boolean { return __defaultAutoSelectFamily; }
export function setDefaultAutoSelectFamily(value: any): void {
    if (typeof value !== 'boolean') {
        throw __typeError('ERR_INVALID_ARG_TYPE', "The 'value' argument must be of type boolean");
    }
    __defaultAutoSelectFamily = value;
}
export function getDefaultAutoSelectFamilyAttemptTimeout(): number {
    return __defaultAutoSelectFamilyAttemptTimeout;
}
export function setDefaultAutoSelectFamilyAttemptTimeout(value: any): void {
    if (typeof value !== 'number') {
        throw __typeError('ERR_INVALID_ARG_TYPE', "The 'value' argument must be of type number");
    }
    if (!Number.isFinite(value) || Math.floor(value) !== value || value < 1 || value > 2147483647) {
        throw __rangeError('ERR_OUT_OF_RANGE', "The value of 'value' is out of range");
    }
    __defaultAutoSelectFamilyAttemptTimeout = value;
}

export default {
    createServer, createConnection, connect,
    isIP, isIPv4, isIPv6,
    Server, Socket, BlockList, SocketAddress,
    getDefaultAutoSelectFamily, setDefaultAutoSelectFamily,
    getDefaultAutoSelectFamilyAttemptTimeout, setDefaultAutoSelectFamilyAttemptTimeout,
};
