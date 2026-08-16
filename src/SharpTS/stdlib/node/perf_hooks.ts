// Node.js 'perf_hooks' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/perf_hooks.html.
//
// High-resolution timing and performance-entry tracking. The only host-tied
// piece is `now()` — a stopwatch-backed read of monotonic ms. Everything else
// (mark, measure, entries storage, PerformanceObserver with synchronous
// callback dispatch) is pure TypeScript, scoped to this module.
//
// timeOrigin is computed at module load: Date.now() minus the first `now()`
// reading, which captures the process's boot-ish wall-clock anchor.

import { now as __now } from 'primitive:perf';

// ─── Internal types ────────────────────────────────────────────────

interface PerformanceEntry {
    name: string;
    entryType: string;
    startTime: number;
    duration: number;
}

interface ObserverRegistration {
    callback: (list: any) => void;
    entryTypes: string[];
    connected: boolean;
}

// ─── Module state ──────────────────────────────────────────────────

// Anchor wall-clock time to the process start. `__now()` returns ms since
// first call; subtracting gives unix ms at module load, matching Node's
// performance.timeOrigin semantics.
const _timeOrigin: number = Date.now() - __now();

const _entries: PerformanceEntry[] = [];
const _observers: ObserverRegistration[] = [];

// ─── Helpers ───────────────────────────────────────────────────────

function createEntry(name: string, entryType: string, startTime: number, duration: number): PerformanceEntry {
    return { name, entryType, startTime, duration };
}

function findMark(name: string): PerformanceEntry | null {
    // Reverse scan — matches the existing behavior of returning the most
    // recent mark when multiple share a name.
    for (let i = _entries.length - 1; i >= 0; i--) {
        const e = _entries[i];
        if (e.entryType === 'mark' && e.name === name) return e;
    }
    return null;
}

function notifyObservers(entry: PerformanceEntry, entryType: string): void {
    for (const reg of _observers) {
        if (!reg.connected) continue;
        if (!reg.entryTypes.includes(entryType)) continue;
        const list = {
            getEntries(): PerformanceEntry[] { return [entry]; }
        };
        reg.callback(list);
    }
}

function clearByType(entryType: string, name?: string): void {
    for (let i = _entries.length - 1; i >= 0; i--) {
        const e = _entries[i];
        if (e.entryType !== entryType) continue;
        if (name != null && e.name !== name) continue;
        _entries.splice(i, 1);
    }
}

// ─── performance ───────────────────────────────────────────────────

/**
 * Node's `performance` singleton. All time values are in milliseconds;
 * fractional precision comes from the host Stopwatch.
 */
export const performance = {
    /** High-resolution monotonic time in ms since performance init. */
    now(): number { return __now(); },

    /** Unix timestamp (ms since epoch) captured at module load. */
    timeOrigin: _timeOrigin,

    /** Creates a PerformanceMark entry and notifies observers. */
    mark(name: string, options?: any): PerformanceEntry {
        let startTime = __now();
        if (options != null && typeof options.startTime === 'number') {
            startTime = options.startTime;
        }
        const entry = createEntry(name, 'mark', startTime, 0);
        _entries.push(entry);
        notifyObservers(entry, 'mark');
        return entry;
    },

    /** Creates a PerformanceMeasure entry between two marks (or from a mark to now). */
    measure(name: string, startMark?: string, endMark?: string): PerformanceEntry {
        let startTime = 0;
        let endTime = __now();
        if (startMark != null) {
            const s = findMark(startMark);
            if (s != null) startTime = s.startTime;
        }
        if (endMark != null) {
            const e = findMark(endMark);
            if (e != null) endTime = e.startTime;
        }
        const duration = endTime - startTime;
        const entry = createEntry(name, 'measure', startTime, duration);
        _entries.push(entry);
        notifyObservers(entry, 'measure');
        return entry;
    },

    /** Returns all recorded entries. */
    getEntries(): PerformanceEntry[] {
        return _entries.slice();
    },

    /** Returns entries matching `name`, optionally filtered by `type`. */
    getEntriesByName(name: string, type?: string): PerformanceEntry[] {
        const result: PerformanceEntry[] = [];
        for (const e of _entries) {
            if (e.name !== name) continue;
            if (type != null && e.entryType !== type) continue;
            result.push(e);
        }
        return result;
    },

    /** Returns entries whose entryType matches `type`. */
    getEntriesByType(type: string): PerformanceEntry[] {
        const result: PerformanceEntry[] = [];
        for (const e of _entries) {
            if (e.entryType === type) result.push(e);
        }
        return result;
    },

    /** Removes mark entries — all, or only those matching `name`. */
    clearMarks(name?: string): void {
        clearByType('mark', name);
    },

    /** Removes measure entries — all, or only those matching `name`. */
    clearMeasures(name?: string): void {
        clearByType('measure', name);
    },
};

// ─── PerformanceObserver ───────────────────────────────────────────

/**
 * Observes performance entries as they're created. Callbacks fire
 * synchronously from within `performance.mark` / `performance.measure`.
 */
export class PerformanceObserver {
    private _reg: ObserverRegistration;

    constructor(callback: (list: any) => void) {
        this._reg = { callback, entryTypes: [], connected: false };
    }

    /** Subscribe to entries of the given types. */
    observe(options: any): void {
        if (options != null && Array.isArray(options.entryTypes)) {
            this._reg.entryTypes = options.entryTypes.slice();
        }
        this._reg.connected = true;
        _observers.push(this._reg);
    }

    /** Stop receiving further entries. */
    disconnect(): void {
        this._reg.connected = false;
    }
}

export default { performance, PerformanceObserver };
