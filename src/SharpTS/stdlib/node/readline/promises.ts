// Node.js 'readline/promises' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. The host readline primitive remains synchronous,
// while this facade exposes the Promise-based Node API shape.

import { createInterface as createHostInterface } from 'primitive:readline';

export class Interface {
    private _inner: any;
    private _closed: boolean;

    constructor(options?: any) {
        this._inner = createHostInterface(options);
        this._closed = false;
    }

    question(query: string, options?: any): Promise<string> {
        if (this._closed) {
            return Promise.reject(new Error('readline was closed'));
        }

        const signal = options != null ? options.signal : undefined;
        if (signal != null && signal.aborted) {
            return Promise.reject(signal.reason != null ? signal.reason : new Error('The operation was aborted'));
        }

        return new Promise((resolve: any, reject: any) => {
            let settled = false;
            const onAbort = () => {
                if (settled) return;
                settled = true;
                reject(signal.reason != null ? signal.reason : new Error('The operation was aborted'));
            };

            if (signal != null) {
                try {
                    signal.addEventListener('abort', onAbort, { once: true });
                } catch (e) {
                    signal.onabort = onAbort;
                }
            }

            this._inner.question(query, (answer: string) => {
                if (settled) return;
                settled = true;
                resolve(answer);
            });
        });
    }

    close(): Interface {
        this._closed = true;
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
}

export function createInterface(options?: any): Interface {
    return new Interface(options);
}

// Node also exports a Readline class for transactional terminal updates. The
// callback facade has no cursor-level host primitive yet, so Phase 1 keeps the
// supported promise surface intentionally limited to Interface/createInterface.
export default { Interface, createInterface };
