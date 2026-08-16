// Node.js 'os' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/os.html.
//
// Heavy lifting (platform detection, memory queries, network interfaces)
// stays in C# behind `primitive:os`. This file is a thin, Node-shape facade:
// every export forwards to its matching primitive. Divergence from Node
// semantics lives in the primitive, not here.

import {
    platform as __platform,
    arch as __arch,
    hostname as __hostname,
    homedir as __homedir,
    tmpdir as __tmpdir,
    type as __type,
    release as __release,
    cpus as __cpus,
    totalmem as __totalmem,
    freemem as __freemem,
    userInfo as __userInfo,
    loadavg as __loadavg,
    networkInterfaces as __networkInterfaces,
    EOL as __EOL,
} from 'primitive:os';

/** CPU information returned by cpus(). */
export interface CpuInfo {
    model: string;
    speed: number;
}

/** User info returned by userInfo(). */
export interface UserInfo {
    username: string;
    uid: number;
    gid: number;
    shell: string | null;
    homedir: string;
}

/** Returns the operating system platform (e.g. 'win32', 'linux', 'darwin'). */
export function platform(): string { return __platform(); }

/** Returns the CPU architecture (e.g. 'x64', 'arm64'). */
export function arch(): string { return __arch(); }

/** Returns the hostname of the operating system. */
export function hostname(): string { return __hostname(); }

/** Returns the current user's home directory. */
export function homedir(): string { return __homedir(); }

/** Returns the operating system's default directory for temporary files. */
export function tmpdir(): string { return __tmpdir(); }

/** Returns the operating system type (e.g. 'Linux', 'Darwin', 'Windows_NT'). */
export function type(): string { return __type(); }

/** Returns the operating system release. */
export function release(): string { return __release(); }

/** Returns an array of objects with information about each logical CPU core. */
export function cpus(): CpuInfo[] { return __cpus(); }

/** Returns the total amount of system memory in bytes. */
export function totalmem(): number { return __totalmem(); }

/** Returns the amount of free system memory in bytes. */
export function freemem(): number { return __freemem(); }

/** Returns information about the currently effective user. */
export function userInfo(): UserInfo { return __userInfo(); }

/** Returns the system load averages as a 3-tuple [1min, 5min, 15min]. */
export function loadavg(): number[] { return __loadavg(); }

/** Returns an object containing network interfaces keyed by interface name. */
export function networkInterfaces(): any { return __networkInterfaces(); }

/** The operating system-specific end-of-line marker ('\n' on POSIX, '\r\n' on Windows). */
export const EOL: string = __EOL;

export default {
    platform, arch, hostname, homedir, tmpdir, type, release,
    cpus, totalmem, freemem, userInfo, loadavg, networkInterfaces, EOL,
};
