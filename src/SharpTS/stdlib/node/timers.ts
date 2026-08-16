// Node.js 'timers' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/timers.html.
//
// Re-exports the callback-based timer API from `primitive:timers`, which
// dispatches to the same $Runtime methods used by the global setTimeout etc.
// The user-facing timers module and the globals share the same runtime
// infrastructure; importing here adds no semantic difference.
//
// Rest-arg note: the setTimeout / setInterval / setImmediate primitives pack
// trailing callback args into an object[]. The facade forwards `...args`
// straight through; the built-in module emitters expand a trailing
// `Expr.Spread` at runtime (see TimersPrimitiveEmitter.EmitArgsArray), so there
// is no arity ceiling.

import {
    setTimeout as __setTimeout,
    clearTimeout as __clearTimeout,
    setInterval as __setInterval,
    clearInterval as __clearInterval,
    setImmediate as __setImmediate,
    clearImmediate as __clearImmediate,
} from 'primitive:timers';

/** Schedules `callback` to run after `delay` ms with optional trailing args. */
export function setTimeout(callback: any, delay?: number, ...args: any[]): any {
    return __setTimeout(callback, delay ?? 0, ...args);
}

/** Cancels a pending timeout created by setTimeout. */
export function clearTimeout(handle?: any): void {
    __clearTimeout(handle);
}

/** Schedules `callback` to repeat every `delay` ms with optional trailing args. */
export function setInterval(callback: any, delay?: number, ...args: any[]): any {
    return __setInterval(callback, delay ?? 0, ...args);
}

/** Cancels a pending interval created by setInterval. */
export function clearInterval(handle?: any): void {
    __clearInterval(handle);
}

/** Schedules `callback` to run on the next event-loop iteration. */
export function setImmediate(callback: any, ...args: any[]): any {
    return __setImmediate(callback, ...args);
}

/** Cancels a pending immediate created by setImmediate. */
export function clearImmediate(handle?: any): void {
    __clearImmediate(handle);
}

export default {
    setTimeout, clearTimeout,
    setInterval, clearInterval,
    setImmediate, clearImmediate,
};
