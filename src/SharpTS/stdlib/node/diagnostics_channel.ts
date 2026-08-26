// Node.js 'diagnostics_channel' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/diagnostics_channel.html.

type ChannelName = string | symbol;
type Subscriber = (message: any, name: ChannelName) => void;

const channels = new Map<any, Channel>();

/** A named synchronous diagnostics channel. */
export class Channel {
    name: ChannelName;
    private _subscribers: any[];
    private _stores: any[];

    constructor(name: ChannelName) {
        this.name = name;
        this._subscribers = [];
        this._stores = [];
    }

    get hasSubscribers(): boolean {
        return this._subscribers.length > 0;
    }

    publish(message: any): void {
        // Snapshot the list so subscribe/unsubscribe during publication only
        // affects the next publication, matching Node's synchronous contract.
        const listeners = this._subscribers.slice();
        for (let i = 0; i < listeners.length; i++) {
            listeners[i](message, this.name);
        }
    }

    subscribe(onMessage: any): void {
        if (typeof onMessage !== 'function') {
            throw new TypeError('The "onMessage" argument must be a function');
        }
        if (this._subscribers.indexOf(onMessage) < 0) {
            this._subscribers.push(onMessage);
        }
    }

    unsubscribe(onMessage: any): boolean {
        const index = this._subscribers.indexOf(onMessage);
        if (index < 0) return false;
        this._subscribers.splice(index, 1);
        return true;
    }

    bindStore(store: any, transform?: (context: any) => any): void {
        this.unbindStore(store);
        this._stores.push([store, transform]);
    }

    unbindStore(store: any): boolean {
        for (let i = 0; i < this._stores.length; i++) {
            if (this._stores[i][0] === store) {
                this._stores.splice(i, 1);
                return true;
            }
        }
        return false;
    }

    runStores(context: any, fn: any, thisArg?: any, ...args: any[]): any {
        if (typeof fn !== 'function') {
            throw new TypeError('The "fn" argument must be a function');
        }

        const bindings = this._stores.slice();
        const invoke = (index: number): any => {
            if (index >= bindings.length) {
                this.publish(context);
                return fn.apply(thisArg, args);
            }
            const binding = bindings[index];
            const store = binding[0];
            const transform = binding[1];
            const value = typeof transform === 'function' ? transform(context) : context;
            return store.run(value, () => invoke(index + 1));
        };

        return invoke(0);
    }
}

/** Returns the singleton Channel for a string or symbol name. */
export function channel(name: ChannelName): Channel {
    let existing: any = channels.get(name);
    if (existing === undefined) {
        existing = new Channel(name);
        channels.set(name, existing);
    }
    return existing as Channel;
}

export function hasSubscribers(name: ChannelName): boolean {
    const existing: any = channels.get(name);
    return existing !== undefined && existing.hasSubscribers;
}

export function subscribe(name: ChannelName, onMessage: any): void {
    channel(name).subscribe(onMessage);
}

export function unsubscribe(name: ChannelName, onMessage: any): boolean {
    return channel(name).unsubscribe(onMessage);
}

/** The five related channels used by tracing helpers. */
export class TracingChannel {
    start: Channel;
    end: Channel;
    asyncStart: Channel;
    asyncEnd: Channel;
    error: Channel;

    constructor(nameOrChannels: any) {
        if (typeof nameOrChannels === 'string' || typeof nameOrChannels === 'symbol') {
            const base = nameOrChannels;
            this.start = channel('tracing:' + String(base) + ':start');
            this.end = channel('tracing:' + String(base) + ':end');
            this.asyncStart = channel('tracing:' + String(base) + ':asyncStart');
            this.asyncEnd = channel('tracing:' + String(base) + ':asyncEnd');
            this.error = channel('tracing:' + String(base) + ':error');
        } else {
            this.start = nameOrChannels.start;
            this.end = nameOrChannels.end;
            this.asyncStart = nameOrChannels.asyncStart;
            this.asyncEnd = nameOrChannels.asyncEnd;
            this.error = nameOrChannels.error;
        }
    }

    get hasSubscribers(): boolean {
        return this.start.hasSubscribers || this.end.hasSubscribers ||
            this.asyncStart.hasSubscribers || this.asyncEnd.hasSubscribers ||
            this.error.hasSubscribers;
    }

    subscribe(handlers: any): void {
        if (handlers.start) this.start.subscribe(handlers.start);
        if (handlers.end) this.end.subscribe(handlers.end);
        if (handlers.asyncStart) this.asyncStart.subscribe(handlers.asyncStart);
        if (handlers.asyncEnd) this.asyncEnd.subscribe(handlers.asyncEnd);
        if (handlers.error) this.error.subscribe(handlers.error);
    }

    unsubscribe(handlers: any): void {
        if (handlers.start) this.start.unsubscribe(handlers.start);
        if (handlers.end) this.end.unsubscribe(handlers.end);
        if (handlers.asyncStart) this.asyncStart.unsubscribe(handlers.asyncStart);
        if (handlers.asyncEnd) this.asyncEnd.unsubscribe(handlers.asyncEnd);
        if (handlers.error) this.error.unsubscribe(handlers.error);
    }

    traceSync(fn: any, context?: any, thisArg?: any, ...args: any[]): any {
        const ctx: any = context != null ? context : {};
        this.start.publish(ctx);
        try {
            const result = fn.apply(thisArg, args);
            ctx.result = result;
            return result;
        } catch (err) {
            ctx.error = err;
            this.error.publish(ctx);
            throw err;
        } finally {
            this.end.publish(ctx);
        }
    }

    async tracePromise(fn: any, context?: any, thisArg?: any, ...args: any[]): Promise<any> {
        const ctx: any = context != null ? context : {};
        this.start.publish(ctx);
        try {
            const promise = fn.apply(thisArg, args);
            this.asyncStart.publish(ctx);
            const result = await promise;
            ctx.result = result;
            return result;
        } catch (err) {
            ctx.error = err;
            this.error.publish(ctx);
            throw err;
        } finally {
            this.asyncEnd.publish(ctx);
            this.end.publish(ctx);
        }
    }

    traceCallback(fn: any, position?: number, context?: any, thisArg?: any, ...args: any[]): any {
        const ctx: any = context != null ? context : {};
        const callbackIndex = position != null ? position : -1;
        if (callbackIndex >= 0 && callbackIndex < args.length) {
            const original = args[callbackIndex];
            args[callbackIndex] = (...callbackArgs: any[]) => {
                this.asyncStart.publish(ctx);
                try {
                    return original.apply(thisArg, callbackArgs);
                } finally {
                    this.asyncEnd.publish(ctx);
                    this.end.publish(ctx);
                }
            };
        }
        this.start.publish(ctx);
        try {
            return fn.apply(thisArg, args);
        } catch (err) {
            ctx.error = err;
            this.error.publish(ctx);
            this.end.publish(ctx);
            throw err;
        }
    }
}

export function tracingChannel(nameOrChannels: any): TracingChannel {
    return new TracingChannel(nameOrChannels);
}

export default {
    Channel,
    TracingChannel,
    channel,
    hasSubscribers,
    subscribe,
    unsubscribe,
    tracingChannel,
};
