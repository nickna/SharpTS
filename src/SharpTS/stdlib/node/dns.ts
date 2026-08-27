// Node.js 'dns' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/dns.html.
//
// Blocking host capabilities and Resolver state stay in C# behind primitive:dns;
// asynchronous resolution comes from primitive:dns/promises. Callback APIs are
// derived here from Promises, giving interpreted and compiled programs one
// implementation for callback timing, error delivery, and result shaping.

import {
    lookup as __lookupSync,
    lookupService as __lookupServiceSync,
    createResolver as __createResolver,
    resolverSetServers as __resolverSetServers,
    resolverGetServers as __resolverGetServers,
    resolverCancel as __resolverCancel,
    resolverGetGeneration as __resolverGetGeneration,
    resolverSetLocalAddress as __resolverSetLocalAddress,
    setDefaultResultOrder as __setDefaultResultOrder,
    getDefaultResultOrder as __getDefaultResultOrder,
} from 'primitive:dns';

import {
    lookup as __lookup,
    lookupService as __lookupService,
    resolve as __resolve,
    resolve4 as __resolve4,
    resolve6 as __resolve6,
    reverse as __reverse,
    resolveMx as __resolveMx,
    resolveTxt as __resolveTxt,
    resolveSrv as __resolveSrv,
    resolveCname as __resolveCname,
    resolveNs as __resolveNs,
    resolveSoa as __resolveSoa,
    resolvePtr as __resolvePtr,
    resolveCaa as __resolveCaa,
    resolveNaptr as __resolveNaptr,
    resolverResolve as __resolverResolve,
} from 'primitive:dns/promises';

function __callbackResult(promise: Promise<any>, callback: any): void {
    promise.then(
        (value: any) => {
            if (value !== null && value !== undefined && value.__dnsError) callback(value, null);
            else callback(null, value);
        },
        (error: any) => { callback(error, null); }
    );
}

function __normalizeLookupAll(promise: Promise<any>, options: any): Promise<any> {
    if (options === undefined || !options.all) return promise;
    return promise.then((value: any) => Array.isArray(value) ? value : [value]);
}

/** Resolve a hostname using the operating-system resolver. */
export function lookup(hostname: string, options?: any, callback?: any): any {
    if (callback === undefined && typeof options === 'function') {
        callback = options;
        options = undefined;
    }

    // Preserve SharpTS's historical direct-return extension when no callback
    // is supplied. Node itself requires the callback form.
    if (callback === undefined) {
        if (options === undefined) return __lookupSync(hostname);
        return __lookupSync(hostname, options);
    }

    const promise = __normalizeLookupAll(
        options === undefined ? __lookup(hostname) : __lookup(hostname, options), options);
    promise.then(
        (value: any) => {
            if (options !== undefined && options.all) callback(null, value);
            else callback(null, value.address, value.family);
        },
        (error: any) => { callback(error, null, null); }
    );
    return null;
}

/** Reverse-resolve an address and service. */
export function lookupService(address: string, port: number, callback?: any): any {
    // Preserve the historical direct-return extension when callback is absent.
    if (callback === undefined) return __lookupServiceSync(address, port);
    __lookupService(address, port).then(
        (value: any) => { callback(null, value.hostname, value.service); },
        (error: any) => { callback(error, null, null); }
    );
    return null;
}

export function resolve(hostname: string, rrtype: any, callback?: any): void {
    if (callback === undefined) {
        callback = rrtype;
        __callbackResult(__resolve(hostname), callback);
        return;
    }
    __callbackResult(__resolve(hostname, rrtype), callback);
}

export function resolve4(hostname: string, callback: any): void { __callbackResult(__resolve4(hostname), callback); }
export function resolve6(hostname: string, callback: any): void { __callbackResult(__resolve6(hostname), callback); }
export function reverse(ip: string, callback: any): void { __callbackResult(__reverse(ip), callback); }
export function resolveMx(hostname: string, callback: any): void { __callbackResult(__resolveMx(hostname), callback); }
export function resolveTxt(hostname: string, callback: any): void { __callbackResult(__resolveTxt(hostname), callback); }
export function resolveSrv(hostname: string, callback: any): void { __callbackResult(__resolveSrv(hostname), callback); }
export function resolveCname(hostname: string, callback: any): void { __callbackResult(__resolveCname(hostname), callback); }
export function resolveNs(hostname: string, callback: any): void { __callbackResult(__resolveNs(hostname), callback); }
export function resolveSoa(hostname: string, callback: any): void { __callbackResult(__resolveSoa(hostname), callback); }
export function resolvePtr(hostname: string, callback: any): void { __callbackResult(__resolvePtr(hostname), callback); }
export function resolveCaa(hostname: string, callback: any): void { __callbackResult(__resolveCaa(hostname), callback); }
export function resolveNaptr(hostname: string, callback: any): void { __callbackResult(__resolveNaptr(hostname), callback); }

/** Stateful DNS resolver with per-instance server configuration. */
export class Resolver {
    private _state: any;

    constructor(_options?: any) {
        this._state = __createResolver();
    }

    setServers(servers: string[]): void { __resolverSetServers(this._state, servers); }
    getServers(): string[] { return __resolverGetServers(this._state); }
    cancel(): void { __resolverCancel(this._state); }

    setLocalAddress(ipv4?: string, ipv6?: string): void {
        __resolverSetLocalAddress(this._state, ipv4, ipv6);
    }

    resolve(hostname: string, rrtype: any, callback?: any): void {
        const generation = __resolverGetGeneration(this._state);
        if (callback === undefined) {
            callback = rrtype;
            __callbackResult(__resolverResolve(this._state, 'resolve', hostname, null, generation), callback);
            return;
        }
        __callbackResult(__resolverResolve(this._state, 'resolve', hostname, rrtype, generation), callback);
    }

    private _query(method: string, identifier: string, callback: any): void {
        const generation = __resolverGetGeneration(this._state);
        __callbackResult(__resolverResolve(this._state, method, identifier, null, generation), callback);
    }

    resolve4(hostname: string, callback: any): void { this._query('resolve4', hostname, callback); }
    resolve6(hostname: string, callback: any): void { this._query('resolve6', hostname, callback); }
    reverse(ip: string, callback: any): void { this._query('reverse', ip, callback); }
    resolveMx(hostname: string, callback: any): void { this._query('resolveMx', hostname, callback); }
    resolveTxt(hostname: string, callback: any): void { this._query('resolveTxt', hostname, callback); }
    resolveSrv(hostname: string, callback: any): void { this._query('resolveSrv', hostname, callback); }
    resolveCname(hostname: string, callback: any): void { this._query('resolveCname', hostname, callback); }
    resolveNs(hostname: string, callback: any): void { this._query('resolveNs', hostname, callback); }
    resolveSoa(hostname: string, callback: any): void { this._query('resolveSoa', hostname, callback); }
    resolvePtr(hostname: string, callback: any): void { this._query('resolvePtr', hostname, callback); }
    resolveCaa(hostname: string, callback: any): void { this._query('resolveCaa', hostname, callback); }
    resolveNaptr(hostname: string, callback: any): void { this._query('resolveNaptr', hostname, callback); }
}

export function setDefaultResultOrder(order: string): void { __setDefaultResultOrder(order); }
export function getDefaultResultOrder(): string { return __getDefaultResultOrder(); }

// Assemble dns.promises from local forwarding functions. Keeping the wrappers
// in this module also avoids exposing primitive method values (compiled
// primitive emitters dispatch calls, not first-class imported method values).
function __promisesLookup(hostname: string, options?: any): Promise<any> {
    if (options === undefined) return __lookup(hostname);
    return __normalizeLookupAll(__lookup(hostname, options), options);
}
function __promisesLookupService(address: string, port: number): Promise<any> { return __lookupService(address, port); }
function __promisesResolve(hostname: string, rrtype?: string): Promise<any> {
    if (rrtype === undefined) return __resolve(hostname);
    return __resolve(hostname, rrtype);
}
function __promisesResolve4(hostname: string): Promise<any> { return __resolve4(hostname); }
function __promisesResolve6(hostname: string): Promise<any> { return __resolve6(hostname); }
function __promisesReverse(ip: string): Promise<any> { return __reverse(ip); }
function __promisesResolveMx(hostname: string): Promise<any> { return __resolveMx(hostname); }
function __promisesResolveTxt(hostname: string): Promise<any> { return __resolveTxt(hostname); }
function __promisesResolveSrv(hostname: string): Promise<any> { return __resolveSrv(hostname); }
function __promisesResolveCname(hostname: string): Promise<any> { return __resolveCname(hostname); }
function __promisesResolveNs(hostname: string): Promise<any> { return __resolveNs(hostname); }
function __promisesResolveSoa(hostname: string): Promise<any> { return __resolveSoa(hostname); }
function __promisesResolvePtr(hostname: string): Promise<any> { return __resolvePtr(hostname); }
function __promisesResolveCaa(hostname: string): Promise<any> { return __resolveCaa(hostname); }
function __promisesResolveNaptr(hostname: string): Promise<any> { return __resolveNaptr(hostname); }

export const promises = {
    lookup: __promisesLookup,
    lookupService: __promisesLookupService,
    resolve: __promisesResolve,
    resolve4: __promisesResolve4,
    resolve6: __promisesResolve6,
    reverse: __promisesReverse,
    resolveMx: __promisesResolveMx,
    resolveTxt: __promisesResolveTxt,
    resolveSrv: __promisesResolveSrv,
    resolveCname: __promisesResolveCname,
    resolveNs: __promisesResolveNs,
    resolveSoa: __promisesResolveSoa,
    resolvePtr: __promisesResolvePtr,
    resolveCaa: __promisesResolveCaa,
    resolveNaptr: __promisesResolveNaptr,
    setDefaultResultOrder,
    getDefaultResultOrder,
};

export const ADDRCONFIG = 1;
export const V4MAPPED = 2;
export const ALL = 4;
export const NODATA = 'ENODATA';
export const FORMERR = 'EFORMERR';
export const SERVFAIL = 'ESERVFAIL';
export const NOTFOUND = 'ENOTFOUND';
export const NOTIMP = 'ENOTIMP';
export const REFUSED = 'EREFUSED';
export const BADQUERY = 'EBADQUERY';
export const BADNAME = 'EBADNAME';
export const BADFAMILY = 'EBADFAMILY';
export const BADRESP = 'EBADRESP';
export const CONNREFUSED = 'ECONNREFUSED';
export const TIMEOUT = 'ETIMEOUT';
export const EOF = 'EEOF';
export const FILE = 'EFILE';
export const NOMEM = 'ENOMEM';
export const DESTRUCTION = 'EDESTRUCTION';
export const BADSTR = 'EBADSTR';
export const BADFLAGS = 'EBADFLAGS';
export const NONAME = 'ENONAME';
export const BADHINTS = 'EBADHINTS';
export const NOTINITIALIZED = 'ENOTINITIALIZED';
export const LOADIPHLPAPI = 'ELOADIPHLPAPI';
export const ADDRGETNETWORKPARAMS = 'EADDRGETNETWORKPARAMS';
export const CANCELLED = 'ECANCELLED';

export default {
    lookup, lookupService, resolve, resolve4, resolve6, reverse,
    resolveMx, resolveTxt, resolveSrv, resolveCname, resolveNs, resolveSoa,
    resolvePtr, resolveCaa, resolveNaptr, Resolver, promises,
    setDefaultResultOrder, getDefaultResultOrder,
    ADDRCONFIG, V4MAPPED, ALL, NODATA, FORMERR, SERVFAIL, NOTFOUND, NOTIMP,
    REFUSED, BADQUERY, BADNAME, BADFAMILY, BADRESP, CONNREFUSED, TIMEOUT,
    EOF, FILE, NOMEM, DESTRUCTION, BADSTR, BADFLAGS, NONAME, BADHINTS,
    NOTINITIALIZED, LOADIPHLPAPI, ADDRGETNETWORKPARAMS, CANCELLED,
};
