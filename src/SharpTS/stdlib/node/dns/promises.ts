// Node.js 'dns/promises' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/dns.html#promises-api.
//
// Host DNS work stays behind primitive:dns/promises. This facade owns the
// public module shape and optional-argument dispatch shared by both runtimes.

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
    setDefaultResultOrder as __setDefaultResultOrder,
    getDefaultResultOrder as __getDefaultResultOrder,
} from 'primitive:dns/promises';

export function lookup(hostname: string, options?: any): Promise<any> {
    if (options === undefined) return __lookup(hostname);
    const promise = __lookup(hostname, options);
    if (!options.all) return promise;
    return promise.then((value: any) => Array.isArray(value) ? value : [value]);
}

export function lookupService(address: string, port: number): Promise<any> {
    return __lookupService(address, port);
}

export function resolve(hostname: string, rrtype?: string): Promise<any> {
    if (rrtype === undefined) return __resolve(hostname);
    return __resolve(hostname, rrtype);
}

export function resolve4(hostname: string): Promise<any> { return __resolve4(hostname); }
export function resolve6(hostname: string): Promise<any> { return __resolve6(hostname); }
export function reverse(ip: string): Promise<any> { return __reverse(ip); }
export function resolveMx(hostname: string): Promise<any> { return __resolveMx(hostname); }
export function resolveTxt(hostname: string): Promise<any> { return __resolveTxt(hostname); }
export function resolveSrv(hostname: string): Promise<any> { return __resolveSrv(hostname); }
export function resolveCname(hostname: string): Promise<any> { return __resolveCname(hostname); }
export function resolveNs(hostname: string): Promise<any> { return __resolveNs(hostname); }
export function resolveSoa(hostname: string): Promise<any> { return __resolveSoa(hostname); }
export function resolvePtr(hostname: string): Promise<any> { return __resolvePtr(hostname); }
export function resolveCaa(hostname: string): Promise<any> { return __resolveCaa(hostname); }
export function resolveNaptr(hostname: string): Promise<any> { return __resolveNaptr(hostname); }

export function setDefaultResultOrder(order: string): void { __setDefaultResultOrder(order); }
export function getDefaultResultOrder(): string { return __getDefaultResultOrder(); }

export default {
    lookup, lookupService, resolve, resolve4, resolve6, reverse,
    resolveMx, resolveTxt, resolveSrv, resolveCname, resolveNs, resolveSoa,
    resolvePtr, resolveCaa, resolveNaptr,
    setDefaultResultOrder, getDefaultResultOrder,
};
