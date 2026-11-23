// Module form of the existing global console surface.

export const log = console.log;
export const info = console.info;
export const debug = console.debug;
export const error = console.error;
export const warn = console.warn;
export function clear(): void { console.clear(); }
export function time(label?: string): void { console.time(label); }
export function timeEnd(label?: string): void { console.timeEnd(label); }
export const timeLog = console.timeLog;
export const assert = console.assert;
export function count(label?: string): void { console.count(label); }
export function countReset(label?: string): void { console.countReset(label); }
export function table(data: any, properties?: string[]): void { console.table(data, properties); }
export function dir(item: any, options?: any): void { console.dir(item, options); }
export const group = console.group;
export const groupCollapsed = console.groupCollapsed;
export function groupEnd(): void { console.groupEnd(); }
export const trace = console.trace;

export default {
    log, info, debug, error, warn, clear, time, timeEnd, timeLog, assert,
    count, countReset, table, dir, group, groupCollapsed, groupEnd, trace,
};
