// Node.js 'readline' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/readline.html.
//
// Readline's Interface needs synchronous stdin access and backs events on a
// host-level EventEmitter, so the backing instance lives in C# (SharpTSReadlineInterface
// in interpreter mode, $ReadlineInterface in compiled mode — both extend the
// shared SharpTS EventEmitter backing). The primitive exposes questionSync
// plus a createInterface factory; the TS Interface class below wraps the
// returned handle and forwards every method dynamically, same pattern as
// stdlib/node/async_hooks.ts.
//
// Node's full readline surface (history, keystroke-level handling, the async
// iterator, readline/promises, etc.) is out of scope — SharpTS has historically
// implemented only the subset below. This migration preserves existing
// behavior exactly.

import {
    questionSync as __questionSync,
    createInterface as __createInterface,
} from 'primitive:readline';

/** Synchronous stdin prompt. Writes the query, reads a line, returns it. */
export function questionSync(query: string): string {
    return __questionSync(query);
}

/** Readline Interface — wraps the host-backed Interface handle. */
export class Interface {
    // The underlying host-backed readline interface (SharpTSReadlineInterface
    // in interpreter mode, $ReadlineInterface in compiled mode). Typed `any`
    // so TS dispatches method calls dynamically against the runtime instance.
    private _inner: any;

    constructor(options?: any) {
        this._inner = __createInterface(options);
    }

    // Readline methods
    question(query: string, callback: any): void {
        this._inner.question(query, callback);
    }

    close(): Interface {
        this._inner.close();
        return this;
    }

    prompt(preserveCursor?: boolean): void {
        this._inner.prompt(preserveCursor);
    }

    pause(): Interface {
        this._inner.pause();
        return this;
    }

    resume(): Interface {
        this._inner.resume();
        return this;
    }

    write(data: string): void {
        this._inner.write(data);
    }

    setPrompt(prompt: string): void {
        this._inner.setPrompt(prompt);
    }

    getPrompt(): string {
        return this._inner.getPrompt();
    }

    // EventEmitter methods — forward directly to the host-backed emitter so
    // events fired from close/pause/resume reach listeners registered here.
    on(eventName: string, listener: any): Interface {
        this._inner.on(eventName, listener);
        return this;
    }

    once(eventName: string, listener: any): Interface {
        this._inner.once(eventName, listener);
        return this;
    }

    off(eventName: string, listener: any): Interface {
        this._inner.off(eventName, listener);
        return this;
    }

    addListener(eventName: string, listener: any): Interface {
        this._inner.addListener(eventName, listener);
        return this;
    }

    removeListener(eventName: string, listener: any): Interface {
        this._inner.removeListener(eventName, listener);
        return this;
    }

    emit(eventName: string, ...args: any[]): boolean {
        // NOTE: host-backed emit takes (eventName, args[]) — SharpTS's TS
        // function call path expands the spread for us.
        return this._inner.emit(eventName, ...args);
    }

    removeAllListeners(eventName?: string): Interface {
        this._inner.removeAllListeners(eventName);
        return this;
    }

    listeners(eventName: string): any[] {
        return this._inner.listeners(eventName);
    }

    listenerCount(eventName: string): number {
        return this._inner.listenerCount(eventName);
    }

    eventNames(): string[] {
        return this._inner.eventNames();
    }
}

/** Creates a new readline Interface. */
export function createInterface(options?: any): Interface {
    return new Interface(options);
}

export default { questionSync, Interface, createInterface };
