// Module form of the existing global console surface.

export function log(...args: any[]): void {
    if (args.length === 0) console.log();
    else if (args.length === 1) console.log(args[0]);
    else if (args.length === 2) console.log(args[0], args[1]);
    else if (args.length === 3) console.log(args[0], args[1], args[2]);
    else console.log(args[0], args[1], args[2], args[3], args.slice(4));
}
export function info(...args: any[]): void {
    if (args.length === 0) console.info();
    else if (args.length === 1) console.info(args[0]);
    else if (args.length === 2) console.info(args[0], args[1]);
    else console.info(args[0], args[1], args[2]);
}
export function debug(...args: any[]): void {
    if (args.length === 0) console.debug();
    else if (args.length === 1) console.debug(args[0]);
    else if (args.length === 2) console.debug(args[0], args[1]);
    else console.debug(args[0], args[1], args[2]);
}
export function error(...args: any[]): void {
    if (args.length === 0) console.error();
    else if (args.length === 1) console.error(args[0]);
    else if (args.length === 2) console.error(args[0], args[1]);
    else console.error(args[0], args[1], args[2]);
}
export function warn(...args: any[]): void {
    if (args.length === 0) console.warn();
    else if (args.length === 1) console.warn(args[0]);
    else if (args.length === 2) console.warn(args[0], args[1]);
    else console.warn(args[0], args[1], args[2]);
}
export function clear(): void { console.clear(); }
export function time(label?: string): void { console.time(label); }
export function timeEnd(label?: string): void { console.timeEnd(label); }
export function timeLog(label?: string, ...args: any[]): void {
    if (args.length === 0) console.timeLog(label);
    else if (args.length === 1) console.timeLog(label, args[0]);
    else console.timeLog(label, args[0], args[1]);
}
export function assert(condition?: boolean, ...args: any[]): void {
    if (args.length === 0) console.assert(condition);
    else if (args.length === 1) console.assert(condition, args[0]);
    else console.assert(condition, args[0], args[1]);
}
export function count(label?: string): void { console.count(label); }
export function countReset(label?: string): void { console.countReset(label); }
export function table(data: any, properties?: string[]): void { console.table(data, properties); }
export function dir(item: any, options?: any): void { console.dir(item, options); }
export function group(...args: any[]): void {
    if (args.length === 0) console.group();
    else if (args.length === 1) console.group(args[0]);
    else console.group(args[0], args[1]);
}
export function groupCollapsed(...args: any[]): void {
    if (args.length === 0) console.groupCollapsed();
    else if (args.length === 1) console.groupCollapsed(args[0]);
    else console.groupCollapsed(args[0], args[1]);
}
export function groupEnd(): void { console.groupEnd(); }
export function trace(...args: any[]): void {
    if (args.length === 0) console.trace();
    else if (args.length === 1) console.trace(args[0]);
    else console.trace(args[0], args[1]);
}

export default {
    log, info, debug, error, warn, clear, time, timeEnd, timeLog, assert,
    count, countReset, table, dir, group, groupCollapsed, groupEnd, trace,
};
