// Node.js 'v8' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. The serialization wire format is SharpTS-private;
// it is intended for serialize()/deserialize() round trips, not interchange
// with Node's V8 binary format.

import { Buffer } from 'buffer';
import { memoryUsage } from 'process';
import { totalmem } from 'os';

function cloneError(message: string): Error {
    const error = new Error(message);
    error.name = 'DataCloneError';
    return error;
}

function encodeGraph(root: any): any {
    const seen = new Map<any, number>();
    const nodes: any[] = [];

    const encode = (value: any): any => {
        if (value === undefined) return { $type: 'undefined' };
        if (value === null || typeof value === 'string' || typeof value === 'boolean') return value;
        if (typeof value === 'number') {
            if (value !== value) return { $type: 'number', value: 'NaN' };
            if (value === Infinity) return { $type: 'number', value: 'Infinity' };
            if (value === -Infinity) return { $type: 'number', value: '-Infinity' };
            if (value === 0 && 1 / value === -Infinity) return { $type: 'number', value: '-0' };
            return value;
        }
        if (typeof value === 'bigint') return { $type: 'bigint', value: String(value) };
        if (typeof value === 'function' || typeof value === 'symbol') {
            throw cloneError('The value could not be cloned');
        }

        const previous = seen.get(value);
        if (previous !== undefined) return { $ref: previous };

        const id = nodes.length;
        seen.set(value, id);
        nodes.push(null);

        if (Buffer.isBuffer(value)) {
            nodes[id] = { kind: 'buffer', value: value.toString('base64') };
        } else if (value instanceof Date) {
            nodes[id] = { kind: 'date', value: value.getTime() };
        } else if (value instanceof RegExp) {
            nodes[id] = { kind: 'regexp', source: value.source, flags: value.flags };
        } else if (value instanceof Map) {
            const entries: any[] = [];
            for (const entry of value) entries.push([encode(entry[0]), encode(entry[1])]);
            nodes[id] = { kind: 'map', entries };
        } else if (value instanceof Set) {
            const values: any[] = [];
            for (const item of value) values.push(encode(item));
            nodes[id] = { kind: 'set', values };
        } else if (Array.isArray(value)) {
            const items: any[] = [];
            for (let i = 0; i < value.length; i++) items.push(encode(value[i]));
            nodes[id] = { kind: 'array', items };
        } else if (value instanceof ArrayBuffer || ArrayBuffer.isView(value)) {
            nodes[id] = { kind: 'bytes', value: Buffer.from(value).toString('base64') };
        } else {
            const properties: any[] = [];
            const keys = Object.keys(value);
            for (let i = 0; i < keys.length; i++) {
                properties.push([keys[i], encode(value[keys[i]])]);
            }
            nodes[id] = { kind: 'object', properties };
        }

        return { $ref: id };
    };

    return { version: 1, root: encode(root), nodes };
}

function decodeGraph(graph: any): any {
    if (graph == null || graph.version !== 1 || !Array.isArray(graph.nodes)) {
        throw new Error('Unable to deserialize cloned data');
    }

    const nodes = graph.nodes;
    const values: any[] = [];

    for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i];
        if (node.kind === 'array') values.push([]);
        else if (node.kind === 'object') values.push({});
        else if (node.kind === 'map') values.push(new Map());
        else if (node.kind === 'set') values.push(new Set());
        else if (node.kind === 'date') values.push(new Date(node.value));
        else if (node.kind === 'regexp') values.push(new RegExp(node.source, node.flags));
        else if (node.kind === 'buffer') values.push(Buffer.from(node.value, 'base64'));
        else if (node.kind === 'bytes') {
            const decoded = Buffer.from(node.value, 'base64');
            const bytes = new Uint8Array(decoded.length);
            for (let j = 0; j < decoded.length; j++) bytes[j] = decoded[j];
            values.push(bytes);
        } else {
            throw new Error('Unknown serialized value kind');
        }
    }

    const decode = (value: any): any => {
        if (value == null || typeof value !== 'object') return value;
        if (value.$ref !== undefined) return values[value.$ref];
        if (value.$type === 'undefined') return undefined;
        if (value.$type === 'bigint') return BigInt(value.value);
        if (value.$type === 'number') {
            if (value.value === 'NaN') return NaN;
            if (value.value === 'Infinity') return Infinity;
            if (value.value === '-Infinity') return -Infinity;
            return -0;
        }
        return value;
    };

    for (let i = 0; i < nodes.length; i++) {
        const node = nodes[i];
        const target = values[i];
        if (node.kind === 'array') {
            for (let j = 0; j < node.items.length; j++) target.push(decode(node.items[j]));
        } else if (node.kind === 'object') {
            for (let j = 0; j < node.properties.length; j++) {
                target[node.properties[j][0]] = decode(node.properties[j][1]);
            }
        } else if (node.kind === 'map') {
            for (let j = 0; j < node.entries.length; j++) {
                target.set(decode(node.entries[j][0]), decode(node.entries[j][1]));
            }
        } else if (node.kind === 'set') {
            for (let j = 0; j < node.values.length; j++) target.add(decode(node.values[j]));
        }
    }

    return decode(graph.root);
}

export function serialize(value: any): any {
    return Buffer.from(JSON.stringify(encodeGraph(value)), 'utf8');
}

export function deserialize(buffer: any): any {
    const input = Buffer.isBuffer(buffer) ? buffer : Buffer.from(buffer);
    return decodeGraph(JSON.parse(input.toString('utf8')));
}

export function getHeapStatistics(): any {
    const usage: any = memoryUsage();
    const heapTotal = usage.heapTotal || 0;
    const heapUsed = usage.heapUsed || 0;
    return {
        total_heap_size: heapTotal,
        total_heap_size_executable: 0,
        total_physical_size: usage.rss || heapTotal,
        total_available_size: totalmem(),
        used_heap_size: heapUsed,
        heap_size_limit: totalmem(),
        malloced_memory: usage.external || 0,
        peak_malloced_memory: usage.external || 0,
        does_zap_garbage: 0,
        number_of_native_contexts: 1,
        number_of_detached_contexts: 0,
        total_global_handles_size: 0,
        used_global_handles_size: 0,
        external_memory: usage.external || 0,
    };
}

export function getHeapSpaceStatistics(): any[] {
    const usage: any = memoryUsage();
    return [{
        space_name: 'managed_heap',
        space_size: usage.heapTotal || 0,
        space_used_size: usage.heapUsed || 0,
        space_available_size: Math.max(0, (usage.heapTotal || 0) - (usage.heapUsed || 0)),
        physical_space_size: usage.rss || usage.heapTotal || 0,
    }];
}

/** .NET has no V8 flag parser; accepted for compatibility and intentionally ignored. */
export function setFlagsFromString(_flags: string): void {}

/** Stable tag for SharpTS's private serializer format, not V8 cached bytecode. */
export function cachedDataVersionTag(): number {
    return 0x53545301;
}

export default {
    serialize,
    deserialize,
    getHeapStatistics,
    getHeapSpaceStatistics,
    setFlagsFromString,
    cachedDataVersionTag,
};
