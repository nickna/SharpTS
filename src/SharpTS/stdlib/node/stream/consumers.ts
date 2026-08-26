// Node.js 'stream/consumers' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. Supports Node Readable async iterators and WHATWG
// ReadableStreams through the same conversion helpers.

import { Buffer } from 'buffer';
import { bufferToArrayBuffer, drainQueuedWebStream } from 'primitive:stream/consumers';

function readIteratorChunks(iterator: any, chunks: any[]): any {
    return iterator.next().then((item: any) => {
        if (item.done) return chunks;
        chunks.push(item.value);
        return readIteratorChunks(iterator, chunks);
    });
}

function readChunks(stream: any): any {
    if (stream == null) {
        return Promise.reject(new TypeError('The stream argument is required'));
    }

    const chunks: any[] = [];
    // Check WHATWG streams first. The emitted implementation has an internal
    // Read method visible to dynamic dispatch; treating that as Node's read()
    // would repeatedly read promises instead of draining queued chunks.
    if (typeof stream.getReader === 'function') {
        return Promise.resolve(drainQueuedWebStream(stream));
    }

    // SharpTS Node Readables expose their queued data synchronously through
    // read(). Once push(null) has closed the stream this drains without an
    // event-loop turn, while the public consumer still resolves a Promise.
    if (typeof stream.read === 'function') {
        let chunk = stream.read();
        while (chunk !== null) {
            chunks.push(chunk);
            chunk = stream.read();
        }
        return Promise.resolve(chunks);
    }

    if (stream[Symbol.asyncIterator] != null) {
        return readIteratorChunks(stream[Symbol.asyncIterator](), chunks);
    }

    return Promise.reject(new TypeError('The value must be a readable stream'));
}

function chunkToBuffer(chunk: any): any {
    if (Buffer.isBuffer(chunk)) return chunk;
    if (typeof chunk === 'string') return Buffer.from(chunk, 'utf8');
    if (chunk instanceof ArrayBuffer) return Buffer.from(chunk);
    if (ArrayBuffer.isView(chunk)) return Buffer.from(chunk);
    if (Array.isArray(chunk)) return Buffer.from(chunk);
    return Buffer.from(String(chunk), 'utf8');
}

export function buffer(stream: any): Promise<any> {
    return readChunks(stream).then((chunks: any[]) => {
        return Buffer.concat(chunks.map((chunk: any) => chunkToBuffer(chunk)));
    });
}

/** Node 24.14+: consumes a stream into a Uint8Array. */
export async function bytes(stream: any): Promise<any> {
    // Buffer is Node's Uint8Array subclass. SharpTS uses its dedicated Buffer
    // backing type, so returning it preserves the byte-view behavior without a
    // redundant copy.
    return await buffer(stream);
}

export function arrayBuffer(stream: any): Promise<ArrayBuffer> {
    return bytes(stream).then((value: any) => bufferToArrayBuffer(value));
}

export async function text(stream: any): Promise<string> {
    const value = await buffer(stream);
    return value.toString('utf8');
}

export async function json(stream: any): Promise<any> {
    return JSON.parse(await text(stream));
}

// The global Blob constructor is intentionally interpreter-only today. Keep
// stream/consumers available in standalone compiled programs with a small
// Blob-compatible value implemented entirely in the embedded stdlib.
class ConsumerBlob {
    private _value: any;
    private _type: string;

    constructor(value: any, type: string = '') {
        this._value = value;
        this._type = type.toLowerCase();
    }

    get size(): number { return this._value.length; }
    get type(): string { return this._type; }

    arrayBuffer(): Promise<ArrayBuffer> {
        return Promise.resolve(bufferToArrayBuffer(this._value));
    }

    bytes(): Promise<Uint8Array> {
        // Buffer is Node's Uint8Array subclass and therefore satisfies the Blob
        // bytes() contract while preserving the native SharpTS backing value.
        return Promise.resolve(this._value as unknown as Uint8Array);
    }

    text(): Promise<string> {
        return Promise.resolve(this._value.toString('utf8'));
    }

    slice(start: number = 0, end: number = this._value.length, contentType: string = ''): any {
        return new ConsumerBlob(this._value.slice(start, end), contentType);
    }

    stream(): ReadableStream {
        const value = this._value;
        return new ReadableStream({
            start(controller: any) {
                controller.enqueue(value);
                controller.close();
            }
        });
    }
}

export function blob(stream: any): Promise<Blob> {
    return bytes(stream).then((value: any) => new ConsumerBlob(value) as unknown as Blob);
}

export default { arrayBuffer, blob, buffer, bytes, json, text };
