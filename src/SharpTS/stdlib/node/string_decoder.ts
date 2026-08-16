// Node.js 'string_decoder' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/string_decoder.html.
//
// Delegates byte → string conversion to Buffer.toString(encoding), which
// the SharpTS runtime already provides. Adds the partial-multibyte-sequence
// tracking that makes StringDecoder the right tool for stream decoders:
// when a chunk ends mid-character, the trailing bytes are held back and
// prepended to the next write.

function normalizeEncoding(encoding: string): string {
    const normalized = encoding.toLowerCase().split('-').join('');
    if (normalized === 'utf8') return 'utf8';
    if (normalized === 'utf16le' || normalized === 'ucs2') return 'utf16le';
    if (normalized === 'latin1' || normalized === 'binary') return 'latin1';
    if (normalized === 'base64') return 'base64';
    if (normalized === 'hex') return 'hex';
    if (normalized === 'ascii') return 'ascii';
    return 'utf8';
}

// Scans from the end of a UTF-8 byte sequence and returns the index at which
// a valid boundary sits. Bytes at and after the returned index form an
// incomplete sequence that must wait for more input. Mirrors Node's decoder.
function findValidUtf8End(buf: Buffer): number {
    const len = buf.length;
    if (len === 0) return 0;
    // A UTF-8 sequence is at most 4 bytes, so scan the last up-to-3 bytes
    // looking for the start of a multibyte sequence that hasn't been completed.
    const maxLookback = len < 3 ? len : 3;
    for (let i = 0; i < maxLookback; i++) {
        const pos = len - 1 - i;
        const b = buf.readUInt8(pos);
        if ((b & 0x80) === 0) {
            // ASCII single-byte — everything up to `len` is valid.
            return len;
        }
        if ((b & 0xE0) === 0xC0) {
            // 2-byte sequence starter: needs 1 continuation byte after pos.
            return len - pos >= 2 ? len : pos;
        }
        if ((b & 0xF0) === 0xE0) {
            // 3-byte sequence starter: needs 2 continuations.
            return len - pos >= 3 ? len : pos;
        }
        if ((b & 0xF8) === 0xF0) {
            // 4-byte sequence starter: needs 3 continuations.
            return len - pos >= 4 ? len : pos;
        }
        // 10xxxxxx — continuation byte. Keep scanning for the starter.
    }
    return len;
}

/** Node.js-compatible StringDecoder. */
export class StringDecoder {
    encoding: string;
    private _pending: Buffer;

    constructor(encoding?: string) {
        this.encoding = normalizeEncoding(encoding != null ? encoding : 'utf8');
        this._pending = Buffer.alloc(0);
    }

    /**
     * Decode a Buffer chunk, returning any completed string output. Trailing
     * bytes belonging to an incomplete multibyte sequence are held for the
     * next `write` or `end` call.
     */
    write(chunk: Buffer): string {
        if (chunk == null || chunk.length === 0) {
            return '';
        }
        // Concatenate any held-back bytes from the previous call.
        let combined: Buffer;
        if (this._pending.length > 0) {
            combined = Buffer.concat([this._pending, chunk]);
        } else {
            combined = chunk;
        }

        let validLen = combined.length;
        if (this.encoding === 'utf8') {
            validLen = findValidUtf8End(combined);
        } else if (this.encoding === 'utf16le') {
            // UTF-16LE requires byte pairs. An odd byte at the end waits.
            if ((combined.length & 1) === 1) {
                validLen = combined.length - 1;
            }
        }
        // latin1/ascii/base64/hex: every byte is self-contained, no holdback.

        if (validLen === combined.length) {
            this._pending = Buffer.alloc(0);
        } else {
            this._pending = combined.slice(validLen);
            combined = combined.slice(0, validLen);
        }

        if (combined.length === 0) return '';
        return combined.toString(this.encoding);
    }

    /**
     * Flush any remaining held-back bytes as a string, optionally after
     * processing one final chunk.
     */
    end(chunk?: Buffer): string {
        let prefix = '';
        if (chunk != null && chunk.length > 0) {
            prefix = this.write(chunk);
        }
        if (this._pending.length === 0) return prefix;
        // Decode whatever remains, even if incomplete (Node's behavior).
        const tail = this._pending.toString(this.encoding);
        this._pending = Buffer.alloc(0);
        return prefix + tail;
    }
}

export default { StringDecoder };
