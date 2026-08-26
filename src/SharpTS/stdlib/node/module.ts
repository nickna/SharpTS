// Node.js 'module' compatibility surface.
// Target: Node.js 24.15.0. The catalog intentionally describes modules that
// SharpTS can actually resolve, so packages can use isBuiltin() as a runtime
// capability probe instead of receiving false positives for unimplemented
// Node modules.

import { createRequire as createHostRequire } from 'primitive:module';

export const builtinModules: string[] = [
    'assert',
    'assert/strict',
    'async_hooks',
    'buffer',
    'child_process',
    'cluster',
    'console',
    'crypto',
    'dgram',
    'diagnostics_channel',
    'dns',
    'dns/promises',
    'events',
    'fs',
    'fs/promises',
    'http',
    'https',
    'module',
    'net',
    'os',
    'path',
    'path/posix',
    'path/win32',
    'perf_hooks',
    'process',
    'querystring',
    'readline',
    'readline/promises',
    'stream',
    'stream/consumers',
    'stream/promises',
    'stream/web',
    'string_decoder',
    'timers',
    'timers/promises',
    'tls',
    'tty',
    'url',
    'util',
    'util/types',
    'v8',
    'vm',
    'worker_threads',
    'zlib',
];

export function isBuiltin(moduleName: string): boolean {
    if (typeof moduleName !== 'string') return false;
    const bare = moduleName.startsWith('node:') ? moduleName.slice(5) : moduleName;
    return builtinModules.includes(bare);
}

// SharpTS materializes a single ESM namespace for embedded built-ins rather
// than maintaining a separate mutable CommonJS export table. There are no
// secondary live bindings to synchronize, so this operation is intentionally
// a no-op.
export function syncBuiltinESMExports(): void {}

/**
 * Creates a CommonJS require function relative to a file path or file URL.
 * Compiled mode supports the canonical `const require = createRequire(...);`
 * pattern with string-literal require specifiers; interpreter mode also honors
 * the supplied base path dynamically.
 */
export function createRequire(filename: string | URL): any {
    return createHostRequire(filename);
}

export default {
    builtinModules,
    isBuiltin,
    syncBuiltinESMExports,
    createRequire,
};
