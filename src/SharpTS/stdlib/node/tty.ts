// Node.js 'tty' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/tty.html.
//
// Host check (is the given FD a TTY?) stays in C# behind `primitive:tty`.
// SharpTS only implements the free function `isatty(fd)`; the ReadStream /
// WriteStream classes are out of scope for v1 — user code that needs them
// should use process.stdin / process.stdout directly.

import { isatty as __isatty } from 'primitive:tty';

/** Returns true if `fd` is attached to a TTY. */
export function isatty(fd: number): boolean {
    return __isatty(fd);
}

export default { isatty };
