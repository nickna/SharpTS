// Node.js 'fs' module — SharpTS embedded stdlib implementation.
// Target: Node.js 24.15.0. See https://nodejs.org/api/fs.html.
//
// Raw filesystem I/O stays in C# behind `primitive:fs` (sync ops, streams,
// watchers, constants) and `primitive:fs/promises` (promise-based async I/O).
// This file is the Node-shape facade:
//   - sync APIs forward straight to `primitive:fs`;
//   - the callback APIs (`fs.readFile(p, cb)`, …) are derived here in TS from
//     the promise primitives, so BOTH execution modes (interpreter + compiled)
//     get identical callback behaviour from one implementation — the compiled
//     path previously had no callback fs at all;
//   - `fs.promises` is assembled from the same promise primitives.
// Divergence from Node semantics lives in the primitives, not here.

import {
    // Existence / read / write
    existsSync as __existsSync,
    readFileSync as __readFileSync,
    writeFileSync as __writeFileSync,
    appendFileSync as __appendFileSync,
    // Delete
    unlinkSync as __unlinkSync,
    // Directories
    mkdirSync as __mkdirSync,
    rmdirSync as __rmdirSync,
    readdirSync as __readdirSync,
    // Metadata
    statRaw as __statRaw,
    lstatRaw as __lstatRaw,
    // Move / copy
    renameSync as __renameSync,
    copyFileSync as __copyFileSync,
    // Access / permissions
    accessSync as __accessSync,
    chmodSync as __chmodSync,
    chownSync as __chownSync,
    lchownSync as __lchownSync,
    // Truncate / links / times
    truncateSync as __truncateSync,
    symlinkSync as __symlinkSync,
    readlinkSync as __readlinkSync,
    realpathSync as __realpathSync,
    utimesSync as __utimesSync,
    // File-descriptor APIs
    openSync as __openSync,
    closeSync as __closeSync,
    readSync as __readSync,
    writeSync as __writeSync,
    fstatRaw as __fstatRaw,
    ftruncateSync as __ftruncateSync,
    // Long-tail fd primitives (#976)
    fsyncSync as __fsyncSync,
    fdPath as __fdPath,
    statfsRaw as __statfsRaw,
    // Directory utilities / hard links
    mkdtempSync as __mkdtempSync,
    linkSync as __linkSync,
    // Stream factories
    createReadStream as __createReadStream,
    createWriteStream as __createWriteStream,
    // Watch APIs
    watch as __watch,
    watchFile as __watchFile,
    unwatchFile as __unwatchFile,
} from 'primitive:fs';

import {
    readFile as __pReadFile,
    writeFile as __pWriteFile,
    appendFile as __pAppendFile,
    stat as __pStat,
    lstat as __pLstat,
    unlink as __pUnlink,
    mkdir as __pMkdir,
    rmdir as __pRmdir,
    readdir as __pReaddir,
    rename as __pRename,
    copyFile as __pCopyFile,
    access as __pAccess,
    chmod as __pChmod,
    truncate as __pTruncate,
    utimes as __pUtimes,
    readlink as __pReadlink,
    realpath as __pRealpath,
    symlink as __pSymlink,
    link as __pLink,
    mkdtemp as __pMkdtemp,
} from 'primitive:fs/promises';

// ---------------------------------------------------------------------------
// Callback derivation helpers.
//
// Node's callback APIs are `(err, data?) => void`. We forward to the promise
// primitive and bridge its resolution/rejection back to the callback. The
// primitive returns a real Promise in both modes (the compiled emitter wraps
// its Task via WrapTaskAsPromise), so `.then` is a proven path here.
// ---------------------------------------------------------------------------

/** Bridge a value-producing promise to a Node `(err, data)` callback. */
function __cbData(p: Promise<any>, callback: any): void {
    p.then((value: any) => { callback(null, value); }, (err: any) => { callback(err); });
}

/** Bridge a void promise to a Node `(err)` callback. */
function __cbVoid(p: Promise<any>, callback: any): void {
    p.then(() => { callback(null); }, (err: any) => { callback(err); });
}

// ===========================================================================
// Synchronous APIs — thin forwarders to primitive:fs.
// ===========================================================================

/** Synchronously tests whether the path exists. Returns false on error (never throws). */
export function existsSync(path: string): boolean { return __existsSync(path); }

// Optional trailing arguments are dispatched by arity rather than forwarded as
// `undefined`: the primitives default by argument count, and forwarding an
// explicit `undefined` would be numeric-converted (crash) or mis-read. Spread
// can't be used — the compiled primitive emitter doesn't expand it (see process.ts).

/** Synchronously reads a file. Returns a Buffer, or a string when an encoding is given. */
export function readFileSync(path: string, options?: any): string | Buffer {
    if (options === undefined) return __readFileSync(path);
    return __readFileSync(path, options);
}

/** Synchronously writes data to a file, replacing the file if it already exists. */
export function writeFileSync(path: string, data: any, options?: any): void {
    if (options === undefined) { __writeFileSync(path, data); return; }
    __writeFileSync(path, data, options);
}

/** Synchronously appends data to a file, creating the file if it does not yet exist. */
export function appendFileSync(path: string, data: any, options?: any): void {
    if (options === undefined) { __appendFileSync(path, data); return; }
    __appendFileSync(path, data, options);
}

/** Synchronously removes a file or symbolic link. */
export function unlinkSync(path: string): void { __unlinkSync(path); }

// --- rm / cp (Node 16.7+ recursive remove/copy). The recursion + option logic is
// pure TS over the sync primitives; the async variants wrap the sync walk in a
// Promise so sync and async behave identically. ---

/** Joins a directory and a child name (forward slash; .NET accepts it on Windows). */
function __joinPath(dir: string, name: string): string {
    if (dir.length === 0) return name;
    const last = dir[dir.length - 1];
    return (last === '/' || last === '\\') ? dir + name : dir + '/' + name;
}

/** Runs a synchronous fs op inside a Promise so the callback/promise variants
 * share the one implementation (resolves/rejects exactly as the sync op does). */
function __promisifyVoid(fn: () => void): Promise<void> {
    return new Promise<void>((resolve: any, reject: any) => {
        try { fn(); resolve(undefined); } catch (e) { reject(e); }
    });
}

/** Like __promisifyVoid but resolves with the sync op's return value. */
function __promisifyValue(fn: () => any): Promise<any> {
    return new Promise<any>((resolve: any, reject: any) => {
        try { resolve(fn()); } catch (e) { reject(e); }
    });
}

/** Synchronously removes files and directories. `{ recursive }` removes a whole
 * tree; `{ force }` makes a nonexistent path a no-op instead of throwing ENOENT. */
export function rmSync(path: string, options?: any): void {
    const recursive = !!(options && options.recursive);
    const force = !!(options && options.force);
    // `force` makes a missing path a no-op; otherwise Node throws ENOENT. Checking
    // existence up front keeps this allocation-free of try/catch.
    if (!existsSync(path)) {
        if (force) return;
        throw __fsError('ENOENT', 'no such file or directory', 'rm', path);
    }
    if (lstatSync(path).isDirectory()) {
        if (!recursive) throw __fsError('ERR_FS_EISDIR', 'Path is a directory', 'rm', path);
        rmdirSync(path, { recursive: true });
    } else {
        unlinkSync(path);
    }
}

/** Synchronously copies `src` to `dest`, recursively when `{ recursive: true }`.
 * Honors `force` (default true), `errorOnExist`, `dereference`, `preserveTimestamps`,
 * and a synchronous `filter(src, dest)` predicate. */
export function cpSync(src: string, dest: string, options?: any): void {
    const opts = options || {};
    if (opts.filter && !opts.filter(src, dest)) return;
    const st: any = opts.dereference ? statSync(src) : lstatSync(src);
    if (st.isDirectory()) {
        if (!opts.recursive) {
            throw __fsError('ERR_FS_EISDIR', 'Recursive option not enabled, cannot copy a directory', 'cp', src);
        }
        mkdirSync(dest, { recursive: true });
        const names: any = readdirSync(src);
        for (let i = 0; i < names.length; i++) {
            cpSync(__joinPath(src, names[i]), __joinPath(dest, names[i]), options);
        }
    } else {
        const force = opts.force !== false; // Node default is to overwrite.
        if (existsSync(dest)) {
            if (opts.errorOnExist) throw __fsError('ERR_FS_CP_EEXIST', 'File already exists', 'cp', dest);
            if (!force) return;
        }
        const parent = __parentDir(dest);
        if (parent && parent !== dest && !existsSync(parent)) mkdirSync(parent, { recursive: true });
        copyFileSync(src, dest);
        if (opts.preserveTimestamps) utimesSync(dest, st.atimeMs / 1000, st.mtimeMs / 1000);
    }
}

/** Synchronously creates a directory. */
// --- mkdir helpers (Node semantics live here in TS; the primitive only does the
// raw, always-recursive directory create, so the facade handles recursive vs not,
// EEXIST/ENOENT, and the recursive return value). ---

/** Minimal parent-directory of a path, separator-agnostic (handles / and \\). */
function __parentDir(p: string): string {
    let end = p.length;
    while (end > 1 && (p[end - 1] === '/' || p[end - 1] === '\\')) end--;
    let i = end - 1;
    while (i >= 0 && p[i] !== '/' && p[i] !== '\\') i--;
    if (i < 0) return '';
    if (i === 0) return p[0];
    return p.slice(0, i);
}

const __errnoFor: any = { ENOENT: -2, EEXIST: -17, EACCES: -13, EBADF: -9, ENOSYS: -38, EISDIR: -21 };

/** Builds a Node-shaped fs error (code/errno/syscall/path) thrown from the facade. */
function __fsError(code: string, message: string, syscall: string, path: string): any {
    const err: any = new Error(code + ': ' + message + ', ' + syscall + " '" + path + "'");
    err.code = code;
    err.errno = __errnoFor[code] !== undefined ? __errnoFor[code] : -1;
    err.syscall = syscall;
    err.path = path;
    return err;
}

/** Synchronously creates a directory. Honors `{ recursive }`: non-recursive throws
 * EEXIST/ENOENT like Node; recursive returns the first directory created (or undefined). */
export function mkdirSync(path: string, options?: any): any {
    const recursive = typeof options === 'object' && options !== null && !!options.recursive;
    if (recursive) {
        let firstCreated: any = undefined;
        if (!__existsSync(path)) {
            let dir = path;
            while (!__existsSync(dir)) {
                firstCreated = dir;
                const parent = __parentDir(dir);
                if (!parent || parent === dir) break;
                dir = parent;
            }
        }
        __mkdirSync(path, options);
        return firstCreated;
    }
    // Non-recursive: Node throws ENOENT if a parent is missing, EEXIST if it exists.
    const parent = __parentDir(path);
    if (parent && parent !== path && !__existsSync(parent)) {
        throw __fsError('ENOENT', 'no such file or directory', 'mkdir', path);
    }
    if (__existsSync(path)) {
        throw __fsError('EEXIST', 'file already exists', 'mkdir', path);
    }
    __mkdirSync(path);
    return undefined;
}

/** Synchronously removes a directory. */
export function rmdirSync(path: string, options?: any): void {
    if (options === undefined) { __rmdirSync(path); return; }
    __rmdirSync(path, options);
}

/** Synchronously reads the contents of a directory. */
export function readdirSync(path: string, options?: any): any {
    if (__wantsFileTypes(options)) {
        const names = __readdirRecursive(options) ? __readdirSync(path, { recursive: true }) : __readdirSync(path);
        return __makeDirents(path, names);
    }
    if (options === undefined) return __readdirSync(path);
    return __readdirSync(path, options);
}

// ===========================================================================
// Stats (#977) — one canonical class built from primitive:fs's flat stat record,
// so statSync / await stat / fstat all return the SAME shape and values. The
// seven is*() predicates derive from the mode bits. `{ bigint: true }` stores the
// numeric fields as BigInt and adds *Ns. Methods live on the prototype, so
// Object.keys(stat) returns only the data fields (Node-faithful).
// ===========================================================================
const __S_IFMT = 0xf000, __S_IFREG = 0x8000, __S_IFDIR = 0x4000, __S_IFLNK = 0xa000,
    __S_IFBLK = 0x6000, __S_IFCHR = 0x2000, __S_IFIFO = 0x1000, __S_IFSOCK = 0xc000;

function __trunc(x: number): number {
    return x < 0 ? Math.ceil(x) : Math.floor(x);
}

class Stats {
    dev: any; ino: any; mode: any; nlink: any; uid: any; gid: any; rdev: any;
    size: any; blksize: any; blocks: any;
    atimeMs: any; mtimeMs: any; ctimeMs: any; birthtimeMs: any;
    atime: any; mtime: any; ctime: any; birthtime: any;
    constructor(r: any, big: boolean) {
        const n = (x: number): any => big ? BigInt(__trunc(x)) : x;
        this.dev = n(r.dev); this.ino = n(r.ino); this.mode = n(r.mode);
        this.nlink = n(r.nlink); this.uid = n(r.uid); this.gid = n(r.gid); this.rdev = n(r.rdev);
        this.size = n(r.size); this.blksize = n(r.blksize); this.blocks = n(r.blocks);
        this.atimeMs = n(r.atimeMs); this.mtimeMs = n(r.mtimeMs);
        this.ctimeMs = n(r.ctimeMs); this.birthtimeMs = n(r.birthtimeMs);
        this.atime = new Date(r.atimeMs); this.mtime = new Date(r.mtimeMs);
        this.ctime = new Date(r.ctimeMs); this.birthtime = new Date(r.birthtimeMs);
        if (big) {
            (this as any).atimeNs = BigInt(__trunc(r.atimeMs)) * 1000000n;
            (this as any).mtimeNs = BigInt(__trunc(r.mtimeMs)) * 1000000n;
            (this as any).ctimeNs = BigInt(__trunc(r.ctimeMs)) * 1000000n;
            (this as any).birthtimeNs = BigInt(__trunc(r.birthtimeMs)) * 1000000n;
        }
    }
    isFile(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFREG; }
    isDirectory(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFDIR; }
    isSymbolicLink(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFLNK; }
    isBlockDevice(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFBLK; }
    isCharacterDevice(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFCHR; }
    isFIFO(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFIFO; }
    isSocket(): boolean { return (Number(this.mode) & __S_IFMT) === __S_IFSOCK; }
}

/** Whether a stat options arg requests BigInt fields (`{ bigint: true }`). */
function __statBig(options: any): boolean {
    return typeof options === 'object' && options !== null && !!options.bigint;
}

/** Builds the canonical Stats object from a raw record. */
function __makeStats(raw: any, big: boolean): Stats { return new Stats(raw, big); }

// ===========================================================================
// Dirent (#977) — one canonical class for readdir({ withFileTypes: true }) in
// both modes. Built in TS from the entry's lstat mode, so the seven is*() are
// methods (Node-shaped) and parentPath/path (Node 20+) are present everywhere.
// ===========================================================================
class Dirent {
    name: string;
    parentPath: string;
    path: string;
    mode: number;
    constructor(name: string, parentPath: string, mode: number) {
        this.name = name; this.parentPath = parentPath; this.path = parentPath; this.mode = mode;
    }
    isFile(): boolean { return (this.mode & __S_IFMT) === __S_IFREG; }
    isDirectory(): boolean { return (this.mode & __S_IFMT) === __S_IFDIR; }
    isSymbolicLink(): boolean { return (this.mode & __S_IFMT) === __S_IFLNK; }
    isBlockDevice(): boolean { return (this.mode & __S_IFMT) === __S_IFBLK; }
    isCharacterDevice(): boolean { return (this.mode & __S_IFMT) === __S_IFCHR; }
    isFIFO(): boolean { return (this.mode & __S_IFMT) === __S_IFIFO; }
    isSocket(): boolean { return (this.mode & __S_IFMT) === __S_IFSOCK; }
}

/** Last path segment (the entry's own name), separator-agnostic. */
function __baseName(p: string): string {
    let end = p.length;
    while (end > 1 && (p[end - 1] === '/' || p[end - 1] === '\\')) end--;
    let i = end - 1;
    while (i >= 0 && p[i] !== '/' && p[i] !== '\\') i--;
    return p.slice(i + 1, end);
}

/** Builds a Dirent for an entry `rel` (a name, or relative path under recursive). */
function __makeDirent(base: string, rel: string): Dirent {
    const full = base + '/' + rel;
    let mode = 0;
    try { mode = __lstatRaw(full).mode; } catch (e) { mode = 0; }
    return new Dirent(__baseName(full), __parentDir(full), mode);
}

/** Whether a readdir options arg requested `{ withFileTypes: true }`. */
function __wantsFileTypes(options: any): boolean {
    return typeof options === 'object' && options !== null && !!options.withFileTypes;
}

/** Whether a readdir options arg requested `{ recursive: true }`. */
function __readdirRecursive(options: any): boolean {
    return typeof options === 'object' && options !== null && !!options.recursive;
}

/** Maps a raw name/relative-path listing to Dirent objects. */
function __makeDirents(base: string, names: any): Dirent[] {
    return names.map((rel: string) => __makeDirent(base, rel));
}

/** Synchronously retrieves the Stats for the path. */
export function statSync(path: string, options?: any): any { return __makeStats(__statRaw(path), __statBig(options)); }

/** Synchronously retrieves the Stats for the path without following symbolic links. */
export function lstatSync(path: string, options?: any): any { return __makeStats(__lstatRaw(path), __statBig(options)); }

/** Synchronously renames (moves) a file or directory. */
export function renameSync(oldPath: string, newPath: string): void { __renameSync(oldPath, newPath); }

/** Synchronously copies a file. */
export function copyFileSync(src: string, dest: string, mode?: number): void { __copyFileSync(src, dest); }

/** Synchronously tests a user's permissions for the path; throws if the check fails. */
export function accessSync(path: string, mode?: number): void {
    if (mode === undefined) { __accessSync(path); return; }
    __accessSync(path, mode);
}

/** Synchronously changes the permissions of a file. */
export function chmodSync(path: string, mode: number): void { __chmodSync(path, mode); }

/** Synchronously changes the owner and group of a file. */
export function chownSync(path: string, uid: number, gid: number): void { __chownSync(path, uid, gid); }

/** Synchronously changes the owner and group of a symbolic link. */
export function lchownSync(path: string, uid: number, gid: number): void { __lchownSync(path, uid, gid); }

/** Synchronously truncates a file to the given length. */
export function truncateSync(path: string, len?: number): void {
    if (len === undefined) { __truncateSync(path); return; }
    __truncateSync(path, len);
}

/** Synchronously creates a symbolic link. */
export function symlinkSync(target: string, path: string, type?: string): void {
    if (type === undefined) { __symlinkSync(target, path); return; }
    __symlinkSync(target, path, type);
}

/** Synchronously reads the value of a symbolic link. */
export function readlinkSync(path: string): string { return __readlinkSync(path); }

/** Synchronously computes the canonical pathname, resolving symbolic links. */
export function realpathSync(path: string): string { return __realpathSync(path); }

/** Synchronously changes the file-system timestamps of the path. */
export function utimesSync(path: string, atime: number, mtime: number): void { __utimesSync(path, atime, mtime); }

/** Synchronously opens a file and returns its file descriptor. */
export function openSync(path: string, flags: any, mode?: number): number {
    if (mode === undefined) return __openSync(path, flags);
    return __openSync(path, flags, mode);
}

/** Synchronously closes a file descriptor. */
export function closeSync(fd: number): void { __closeSync(fd); }

/** Synchronously reads from a file descriptor into a buffer; returns the number of bytes read. */
export function readSync(fd: number, buffer: Buffer, offset: number, length: number, position: any): number {
    return __readSync(fd, buffer, offset, length, position);
}

/** Synchronously writes a buffer (or string) to a file descriptor; returns the number of bytes written. */
export function writeSync(fd: number, buffer: any, offset?: number, length?: number, position?: any): number {
    if (offset === undefined) return __writeSync(fd, buffer);
    if (length === undefined) return __writeSync(fd, buffer, offset);
    if (position === undefined) return __writeSync(fd, buffer, offset, length);
    return __writeSync(fd, buffer, offset, length, position);
}

/** Synchronously retrieves the Stats for an open file descriptor. */
export function fstatSync(fd: number, options?: any): any { return __makeStats(__fstatRaw(fd), __statBig(options)); }

/** Synchronously truncates an open file descriptor to the given length. */
export function ftruncateSync(fd: number, len?: number): void {
    if (len === undefined) { __ftruncateSync(fd); return; }
    __ftruncateSync(fd, len);
}

// ===========================================================================
// Long-tail sync ops (#976). Durability (fsync/fdatasync), fd-variant metadata
// (fchmod/fchown/futimes — routed through fdPath to the path ops), symlink
// metadata (lchmod/lutimes), vectored I/O (readv/writev), filesystem stats
// (statfs), and glob. All TS over primitive:fs, so both modes share one impl.
// ===========================================================================

/** Synchronously flushes an fd's buffered writes to disk. */
export function fsyncSync(fd: number): void { __fsyncSync(fd); }

/** Synchronously flushes an fd's data writes. A full flush is a correct superset. */
export function fdatasyncSync(fd: number): void { __fsyncSync(fd); }

/** Synchronously changes the permissions of an open fd's file. */
export function fchmodSync(fd: number, mode: number): void { __chmodSync(__fdPath(fd), mode); }

/** Synchronously changes the owner and group of an open fd's file. */
export function fchownSync(fd: number, uid: number, gid: number): void { __chownSync(__fdPath(fd), uid, gid); }

/** Synchronously changes the file-system timestamps of an open fd's file. */
export function futimesSync(fd: number, atime: any, mtime: any): void { __utimesSync(__fdPath(fd), atime, mtime); }

/** Synchronously changes the permissions of a symbolic link. Only BSD/macOS support
 * this; elsewhere Node fails with ENOSYS, which we surface consistently. */
export function lchmodSync(path: string, mode: number): void {
    throw __fsError('ENOSYS', 'function not implemented', 'lchmod', path);
}

/** Synchronously changes the timestamps of a symbolic link. The BCL has no
 * no-follow timestamp API, so this approximates by setting the target's times. */
export function lutimesSync(path: string, atime: any, mtime: any): void { __utimesSync(path, atime, mtime); }

/** Synchronously reads sequentially into an array of buffers; returns total bytes read. */
export function readvSync(fd: number, buffers: any[], position?: any): number {
    let pos = (position === undefined || position === null) ? null : position;
    let total = 0;
    for (const b of buffers) {
        const n = readSync(fd, b, 0, b.length, pos);
        total += n;
        if (pos !== null) pos += n;
        if (n < b.length) break; // short read => EOF
    }
    return total;
}

/** Synchronously writes an array of buffers sequentially; returns total bytes written. */
export function writevSync(fd: number, buffers: any[], position?: any): number {
    let pos = (position === undefined || position === null) ? null : position;
    let total = 0;
    for (const b of buffers) {
        const n = writeSync(fd, b, 0, b.length, pos);
        total += n;
        if (pos !== null) pos += n;
    }
    return total;
}

/** Shapes the flat statfs record, converting fields to BigInt when requested. */
function __shapeStatfs(raw: any, options: any): any {
    const big = typeof options === 'object' && options !== null && !!options.bigint;
    if (!big) return raw;
    const b = (x: number): any => BigInt(__trunc(x));
    return {
        type: b(raw.type), bsize: b(raw.bsize), blocks: b(raw.blocks), bfree: b(raw.bfree),
        bavail: b(raw.bavail), files: b(raw.files), ffree: b(raw.ffree),
    };
}

/** Synchronously retrieves filesystem statistics for the path. */
export function statfsSync(path: string, options?: any): any { return __shapeStatfs(__statfsRaw(path), options); }

// --- Minimal glob (Node 22+): supports `*` and `?` within a path segment and
// `**` across segments. Matching walks the directory tree under cwd. ---

/** Compiles one glob path-segment (`*`/`?`/literals) to an anchored RegExp. */
function __globSegRegex(seg: string): any {
    let re = '^';
    for (const ch of seg) {
        if (ch === '*') re += '[^/]*';
        else if (ch === '?') re += '[^/]';
        else if ('.+^${}()|[]\\/'.indexOf(ch) >= 0) re += '\\' + ch;
        else re += ch;
    }
    return new RegExp(re + '$');
}

/** Lists a directory, returning [] when it can't be read (not a dir / missing).
 * The catch only assigns — a `return` inside a compiled catch can miscompile (#973). */
function __globReaddir(dir: string): string[] {
    let entries: string[] = [];
    try { entries = __readdirSync(dir); } catch (e) { entries = []; }
    return entries;
}

/** Recursive glob walk: matches `segs[i]` against entries of `base/rel`, collecting
 * matched relative paths into `out`. `**` matches zero or more directory levels. */
function __globWalk(base: string, rel: string, segs: string[], i: number, out: string[]): void {
    if (i >= segs.length) { if (rel.length > 0) out.push(rel); return; }
    const seg = segs[i];
    const dir = rel.length > 0 ? base + '/' + rel : base;
    const entries = __globReaddir(dir);
    if (seg === '**') {
        __globWalk(base, rel, segs, i + 1, out); // ** consumes zero levels
        for (const e of entries) {
            const childRel = rel.length > 0 ? rel + '/' + e : e;
            let isDir = false;
            try { isDir = statSync(base + '/' + childRel).isDirectory(); } catch (er) { isDir = false; }
            if (isDir) __globWalk(base, childRel, segs, i, out); // ...or one+ levels
        }
        return;
    }
    const re = __globSegRegex(seg);
    for (const e of entries) {
        if (re.test(e)) {
            const childRel = rel.length > 0 ? rel + '/' + e : e;
            __globWalk(base, childRel, segs, i + 1, out);
        }
    }
}

/** Synchronously returns paths matching a glob pattern (or array of patterns),
 * relative to `options.cwd` (default `.`). */
export function globSync(pattern: any, options?: any): string[] {
    const pats: any[] = Array.isArray(pattern) ? pattern : [pattern];
    const cwd = (options && typeof options === 'object' && options.cwd) ? options.cwd : '.';
    const out: string[] = [];
    const seen: any = {};
    for (const pat of pats) {
        const segs = ('' + pat).split('/').filter((s: string) => s.length > 0);
        const local: string[] = [];
        __globWalk(cwd, '', segs, 0, local);
        for (const m of local) { if (!seen[m]) { seen[m] = true; out.push(m); } }
    }
    return out;
}

/** Wraps an array as a one-shot async iterable (for fsPromises.glob, Node 22+). */
function __arrayAsyncIterator(items: any[]): any {
    let i = 0;
    return {
        [Symbol.asyncIterator](): any { return this; },
        next(): any {
            if (i < items.length) return Promise.resolve({ value: items[i++], done: false });
            return Promise.resolve({ value: undefined, done: true });
        }
    };
}

/** Synchronously creates a unique temporary directory and returns its path. */
export function mkdtempSync(prefix: string): string { return __mkdtempSync(prefix); }

/** Synchronously opens a directory and returns a Dir handle. */
// A Dir handle: sync + async iterable over a snapshot of the directory's Dirents.
// Node reads lazily; reading the whole listing up front is simple and correct for
// typical sizes. read()/readSync()/iteration share one cursor (as in Node).
function __makeDir(path: string, entries: any[]): any {
    let i = 0;
    return {
        path,
        readSync(): any { return i < entries.length ? entries[i++] : null; },
        read(): Promise<any> { return Promise.resolve(i < entries.length ? entries[i++] : null); },
        closeSync(): void { i = entries.length; },
        close(): Promise<void> { i = entries.length; return Promise.resolve(); },
        [Symbol.asyncIterator](): any {
            return {
                next(): any {
                    const e = i < entries.length ? entries[i++] : null;
                    return Promise.resolve(e !== null ? { value: e, done: false } : { value: undefined, done: true });
                }
            };
        }
    };
}

/** Synchronously opens a directory and returns a Dir handle (sync + async iterable). */
export function opendirSync(path: string, options?: any): any {
    return __makeDir(path, readdirSync(path, { withFileTypes: true } as any));
}

// --- promises.watch: bridge the FSWatcher's 'change' event stream into a
// pull-based async iterator. Events arriving between pulls are queued; a pull
// with no pending event parks a resolver until the next event. An AbortSignal
// (or iterator return()) closes the watcher and ends iteration cleanly so the
// event loop can drain. ---
function __watchAsync(filename: string, options?: any): any {
    const opts = options || {};
    const signal = opts.signal;
    const watcher: any = __watch(filename, opts);
    const queue: any[] = [];
    const resolvers: any[] = [];
    let done = false;

    const finish = () => {
        if (done) return;
        done = true;
        try { watcher.close(); } catch (e) { }
        while (resolvers.length) { resolvers.shift()({ value: undefined, done: true }); }
    };

    watcher.on('change', (eventType: any, fname: any) => {
        if (done) return;
        const ev = { eventType, filename: fname };
        if (resolvers.length) { resolvers.shift()({ value: ev, done: false }); }
        else { queue.push(ev); }
    });
    watcher.on('error', () => { finish(); });

    if (signal) {
        if (signal.aborted) {
            finish();
        } else {
            // Immediate termination when the AbortSignal fires while the iterator is
            // parked — `finish` wakes any parked resolver so the loop ends without a
            // further pull. Works in both modes now that a dynamically-typed signal's
            // addEventListener is callable from stdlib code (#985). The try/catch and
            // the `signal.aborted` poll in next() remain as defensive fallbacks.
            try { signal.addEventListener('abort', finish); } catch (e) { }
        }
    }

    return {
        [Symbol.asyncIterator]() { return this; },
        next(): any {
            if (signal && signal.aborted) finish();
            if (queue.length) return Promise.resolve({ value: queue.shift(), done: false });
            if (done) return Promise.resolve({ value: undefined, done: true });
            return new Promise((resolve: any) => { resolvers.push(resolve); });
        },
        return(): any { finish(); return Promise.resolve({ value: undefined, done: true }); }
    };
}

/** Synchronously creates a hard link. */
export function linkSync(existingPath: string, newPath: string): void { __linkSync(existingPath, newPath); }

/** Returns a new ReadStream for the given path. */
export function createReadStream(path: string, options?: any): any { return __createReadStream(path, options); }

/** Returns a new WriteStream for the given path. */
export function createWriteStream(path: string, options?: any): any { return __createWriteStream(path, options); }

/** Watches for changes on a file or directory; returns an FSWatcher. */
export function watch(filename: string, options?: any, listener?: any): any { return __watch(filename, options, listener); }

/** Watches for changes on a file by polling its stats. */
export function watchFile(filename: string, options: any, listener?: any): any { return __watchFile(filename, options, listener); }

/** Stops watching a file previously registered with watchFile. */
export function unwatchFile(filename: string, listener?: any): void { __unwatchFile(filename, listener); }

/**
 * File-system constants (access modes, open flags, copy flags, file-type bits,
 * permission bits, libuv flags). Defined here so `fs.constants` and
 * `fs.promises.constants` share one complete table across both execution modes.
 * Values follow the POSIX/Linux set SharpTS's openSync flag parsing expects.
 */
export const constants: any = {
    // Access modes (accessSync)
    F_OK: 0, R_OK: 4, W_OK: 2, X_OK: 1,
    // Open flags (openSync)
    O_RDONLY: 0, O_WRONLY: 1, O_RDWR: 2,
    O_CREAT: 64, O_EXCL: 128, O_NOCTTY: 256, O_TRUNC: 512,
    O_APPEND: 1024, O_NONBLOCK: 2048, O_DSYNC: 4096,
    O_DIRECTORY: 65536, O_NOFOLLOW: 131072, O_NOATIME: 262144,
    O_SYNC: 1052672,
    // Copy flags (copyFile)
    COPYFILE_EXCL: 1, COPYFILE_FICLONE: 2, COPYFILE_FICLONE_FORCE: 4,
    // File-type bits (stat mode)
    S_IFMT: 61440, S_IFREG: 32768, S_IFDIR: 16384, S_IFCHR: 8192,
    S_IFBLK: 24576, S_IFIFO: 4096, S_IFLNK: 40960, S_IFSOCK: 49152,
    // Permission bits (stat mode)
    S_IRWXU: 448, S_IRUSR: 256, S_IWUSR: 128, S_IXUSR: 64,
    S_IRWXG: 56, S_IRGRP: 32, S_IWGRP: 16, S_IXGRP: 8,
    S_IRWXO: 7, S_IROTH: 4, S_IWOTH: 2, S_IXOTH: 1,
    // libuv flags
    UV_FS_O_FILEMAP: 0, UV_FS_SYMLINK_DIR: 1, UV_FS_SYMLINK_JUNCTION: 2,
};

// ===========================================================================
// Callback-based async APIs — derived from the promise primitives.
// The trailing argument is the callback; an `options` argument may be omitted.
// ===========================================================================

/** Asynchronously reads the entire contents of a file. */
export function readFile(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__pReadFile(path, options), callback);
}

/** Asynchronously writes data to a file, replacing the file if it already exists. */
export function writeFile(path: string, data: any, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbVoid(__pWriteFile(path, data, options), callback);
}

/** Asynchronously appends data to a file, creating the file if it does not yet exist. */
export function appendFile(path: string, data: any, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbVoid(__pAppendFile(path, data, options), callback);
}

/** Asynchronously retrieves the Stats for the path. */
export function stat(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    const big = __statBig(options);
    __pStat(path).then((r: any) => { callback(null, __makeStats(r, big)); }, (e: any) => { callback(e); });
}

/** Asynchronously retrieves the Stats for the path without following symbolic links. */
export function lstat(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    const big = __statBig(options);
    __pLstat(path).then((r: any) => { callback(null, __makeStats(r, big)); }, (e: any) => { callback(e); });
}

/** Asynchronously removes a file or symbolic link. */
export function unlink(path: string, callback: any): void {
    __cbVoid(__pUnlink(path), callback);
}

/** Asynchronously removes files and directories. */
export function rm(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbVoid(__promisifyVoid(() => rmSync(path, options)), callback);
}

/** Asynchronously copies src to dest (recursively when `{ recursive: true }`). */
export function cp(src: string, dest: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbVoid(__promisifyVoid(() => cpSync(src, dest, options)), callback);
}

/** Asynchronously creates a directory. */
export function mkdir(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__pMkdir(path, options), callback);
}

/** Asynchronously removes a directory. */
export function rmdir(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbVoid(__pRmdir(path, options), callback);
}

/** Asynchronously reads the contents of a directory. */
export function readdir(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    if (__wantsFileTypes(options)) {
        const names = __readdirRecursive(options) ? __pReaddir(path, { recursive: true }) : __pReaddir(path);
        names.then((n: any) => { callback(null, __makeDirents(path, n)); }, (e: any) => { callback(e); });
        return;
    }
    __cbData(__pReaddir(path, options), callback);
}

/** Asynchronously renames (moves) a file or directory. */
export function rename(oldPath: string, newPath: string, callback: any): void {
    __cbVoid(__pRename(oldPath, newPath), callback);
}

/** Asynchronously copies a file. */
export function copyFile(src: string, dest: string, mode?: any, callback?: any): void {
    if (typeof mode === 'function') { callback = mode; mode = undefined; }
    __cbVoid(__pCopyFile(src, dest, mode), callback);
}

/** Asynchronously tests a user's permissions for the path. */
export function access(path: string, mode?: any, callback?: any): void {
    if (typeof mode === 'function') { callback = mode; mode = undefined; }
    __cbVoid(__pAccess(path, mode), callback);
}

/** Asynchronously changes the permissions of a file. */
export function chmod(path: string, mode: number, callback: any): void {
    __cbVoid(__pChmod(path, mode), callback);
}

/** Asynchronously truncates a file to the given length. */
export function truncate(path: string, len?: any, callback?: any): void {
    if (typeof len === 'function') { callback = len; len = undefined; }
    __cbVoid(__pTruncate(path, len), callback);
}

/** Asynchronously changes the file-system timestamps of the path. */
export function utimes(path: string, atime: number, mtime: number, callback: any): void {
    __cbVoid(__pUtimes(path, atime, mtime), callback);
}

/** Asynchronously reads the value of a symbolic link. */
export function readlink(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__pReadlink(path), callback);
}

/** Asynchronously computes the canonical pathname, resolving symbolic links. */
export function realpath(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__pRealpath(path), callback);
}

/** Asynchronously creates a symbolic link. */
export function symlink(target: string, path: string, type?: any, callback?: any): void {
    if (typeof type === 'function') { callback = type; type = undefined; }
    __cbVoid(__pSymlink(target, path, type), callback);
}

/** Asynchronously creates a hard link. */
export function link(existingPath: string, newPath: string, callback: any): void {
    __cbVoid(__pLink(existingPath, newPath), callback);
}

/** Asynchronously creates a unique temporary directory. */
export function mkdtemp(prefix: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__pMkdtemp(prefix), callback);
}

// --- async chown/lchown + callback file-descriptor ops (#974). Thin TS wrappers
// over the *Sync primitives; the Promise wrap defers the callback off the caller's
// synchronous frame and carries err.code (e.g. EBADF) through to the callback. ---

/** Asynchronously changes the owner and group of a file. */
export function chown(path: string, uid: number, gid: number, callback: any): void {
    __cbVoid(__promisifyVoid(() => chownSync(path, uid, gid)), callback);
}

/** Asynchronously changes the owner and group of a symbolic link. */
export function lchown(path: string, uid: number, gid: number, callback: any): void {
    __cbVoid(__promisifyVoid(() => lchownSync(path, uid, gid)), callback);
}

/** Asynchronously opens a file; callback receives (err, fd). */
export function open(path: string, flags?: any, mode?: any, callback?: any): void {
    if (typeof flags === 'function') { callback = flags; flags = 'r'; mode = undefined; }
    else if (typeof mode === 'function') { callback = mode; mode = undefined; }
    __cbData(__promisifyValue(() => mode === undefined ? openSync(path, flags) : openSync(path, flags, mode)), callback);
}

/** Asynchronously closes a file descriptor; callback receives (err). */
export function close(fd: number, callback: any): void {
    __cbVoid(__promisifyVoid(() => closeSync(fd)), callback);
}

/** Asynchronously reads from a file descriptor; callback receives (err, bytesRead, buffer). */
export function read(fd: number, buffer: any, offset: number, length: number, position: any, callback: any): void {
    __promisifyValue(() => readSync(fd, buffer, offset, length, position)).then(
        (n: any) => { callback(null, n, buffer); },
        (e: any) => { callback(e, 0, buffer); }
    );
}

/** Asynchronously writes to a file descriptor; callback receives (err, bytesWritten, buffer). */
export function write(fd: number, buffer: any, offset?: any, length?: any, position?: any, callback?: any): void {
    let cb = callback;
    if (typeof offset === 'function') { cb = offset; offset = undefined; length = undefined; position = undefined; }
    else if (typeof length === 'function') { cb = length; length = undefined; position = undefined; }
    else if (typeof position === 'function') { cb = position; position = undefined; }
    __promisifyValue(() => writeSync(fd, buffer, offset, length, position)).then(
        (n: any) => { cb(null, n, buffer); },
        (e: any) => { cb(e, 0, buffer); }
    );
}

/** Asynchronously retrieves the Stats for an open fd; callback receives (err, stats). */
export function fstat(fd: number, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__promisifyValue(() => fstatSync(fd)), callback);
}

/** Asynchronously truncates an open fd to a length; callback receives (err). */
export function ftruncate(fd: number, len?: any, callback?: any): void {
    if (typeof len === 'function') { callback = len; len = undefined; }
    __cbVoid(__promisifyVoid(() => ftruncateSync(fd, len)), callback);
}

// --- Long-tail callback ops (#976), derived from the sync forms above. ---

/** Asynchronously flushes an fd's buffered writes; callback receives (err). */
export function fsync(fd: number, callback: any): void { __cbVoid(__promisifyVoid(() => fsyncSync(fd)), callback); }

/** Asynchronously flushes an fd's data writes; callback receives (err). */
export function fdatasync(fd: number, callback: any): void { __cbVoid(__promisifyVoid(() => fdatasyncSync(fd)), callback); }

/** Asynchronously changes the permissions of an open fd's file; callback receives (err). */
export function fchmod(fd: number, mode: number, callback: any): void { __cbVoid(__promisifyVoid(() => fchmodSync(fd, mode)), callback); }

/** Asynchronously changes the owner and group of an open fd's file; callback receives (err). */
export function fchown(fd: number, uid: number, gid: number, callback: any): void { __cbVoid(__promisifyVoid(() => fchownSync(fd, uid, gid)), callback); }

/** Asynchronously changes the timestamps of an open fd's file; callback receives (err). */
export function futimes(fd: number, atime: any, mtime: any, callback: any): void { __cbVoid(__promisifyVoid(() => futimesSync(fd, atime, mtime)), callback); }

/** Asynchronously changes the permissions of a symbolic link; callback receives (err). */
export function lchmod(path: string, mode: number, callback: any): void { __cbVoid(__promisifyVoid(() => lchmodSync(path, mode)), callback); }

/** Asynchronously changes the timestamps of a symbolic link; callback receives (err). */
export function lutimes(path: string, atime: any, mtime: any, callback: any): void { __cbVoid(__promisifyVoid(() => lutimesSync(path, atime, mtime)), callback); }

/** Asynchronously reads into an array of buffers; callback receives (err, bytesRead, buffers). */
export function readv(fd: number, buffers: any[], position?: any, callback?: any): void {
    let cb = callback;
    if (typeof position === 'function') { cb = position; position = undefined; }
    __promisifyValue(() => readvSync(fd, buffers, position)).then(
        (n: any) => { cb(null, n, buffers); }, (e: any) => { cb(e, 0, buffers); });
}

/** Asynchronously writes an array of buffers; callback receives (err, bytesWritten, buffers). */
export function writev(fd: number, buffers: any[], position?: any, callback?: any): void {
    let cb = callback;
    if (typeof position === 'function') { cb = position; position = undefined; }
    __promisifyValue(() => writevSync(fd, buffers, position)).then(
        (n: any) => { cb(null, n, buffers); }, (e: any) => { cb(e, 0, buffers); });
}

/** Asynchronously retrieves filesystem statistics; callback receives (err, stats). */
export function statfs(path: string, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__promisifyValue(() => statfsSync(path, options)), callback);
}

/** Asynchronously returns paths matching a glob pattern; callback receives (err, matches). */
export function glob(pattern: any, options?: any, callback?: any): void {
    if (typeof options === 'function') { callback = options; options = undefined; }
    __cbData(__promisifyValue(() => globSync(pattern, options)), callback);
}

/** Deprecated. Tests existence; the callback receives a single boolean (not err-first). */
export function exists(path: string, callback: any): void {
    __promisifyValue(() => existsSync(path)).then((b: any) => { callback(b); }, () => { callback(false); });
}

// ===========================================================================
// FileHandle (#972) — the promise-based file-descriptor workflow.
//
// `fsPromises.open()` resolves a FileHandle wrapping an open fd. Each method
// forwards to the existing fd ops (read/write/stat/truncate/close) wrapped in a
// Promise. The path-scoped conveniences Node also exposes on a handle —
// readFile/writeFile/appendFile/chmod/chown/utimes and the stream factories —
// re-derive from the path the handle was opened with. Pure TS over the
// primitives, so the interpreter and compiled modes are identical by
// construction.
//
// Divergence note: Node backs createReadStream/createWriteStream/appendFile/
// chmod/chown/utimes by the handle's own fd (shared position, auto-close with
// the handle). SharpTS re-derives those from the originating path — same file,
// independent descriptor. The core fd ops (read/write/stat/truncate/close)
// share the handle's descriptor exactly, so the open→read/write→stat→truncate→
// close round-trip is faithful.
// ===========================================================================

/** Extracts a string encoding from a read/readFile options argument (or null). */
function __encodingOf(options: any): any {
    if (typeof options === 'string') return options;
    if (options !== null && typeof options === 'object' && options.encoding) return options.encoding;
    return null;
}

class FileHandle {
    fd: number;
    private __path: string;
    private __closed: boolean;
    constructor(fd: number, path: string) {
        this.fd = fd;
        this.__path = path;
        this.__closed = false;
    }

    /** Reads into a buffer; resolves `{ bytesRead, buffer }`. Accepts the
     *  positional `(buffer, offset, length, position)` form or `(options)`. */
    read(buffer?: any, offset?: any, length?: any, position?: any): Promise<any> {
        let buf = buffer, off = offset, len = length, pos = position;
        // read(options): a lone, non-Buffer object holds { buffer, offset, length, position }.
        if (buffer !== null && buffer !== undefined && typeof buffer === 'object' && !Buffer.isBuffer(buffer)) {
            buf = buffer.buffer; off = buffer.offset; len = buffer.length; pos = buffer.position;
        }
        if (buf === undefined || buf === null) buf = Buffer.alloc(16384);
        if (off === undefined || off === null) off = 0;
        if (len === undefined || len === null) len = buf.length - off;
        if (pos === undefined) pos = null;
        return __promisifyValue(() => readSync(this.fd, buf, off, len, pos))
            .then((bytesRead: any) => ({ bytesRead, buffer: buf }));
    }

    /** Writes a buffer or string; resolves `{ bytesWritten, buffer }`. */
    write(data: any, a?: any, b?: any, c?: any): Promise<any> {
        if (typeof data === 'string') {
            // write(string, position?, encoding?) — the primitive's string path writes UTF-8.
            return __promisifyValue(() => (a === undefined ? writeSync(this.fd, data) : writeSync(this.fd, data, a)))
                .then((bytesWritten: any) => ({ bytesWritten, buffer: data }));
        }
        return __promisifyValue(() => writeSync(this.fd, data, a, b, c))
            .then((bytesWritten: any) => ({ bytesWritten, buffer: data }));
    }

    /** Reads sequentially into an array of buffers; resolves `{ bytesRead, buffers }`. */
    readv(buffers: any[], position?: any): Promise<any> {
        return __promisifyValue(() => {
            let pos = (position === undefined || position === null) ? null : position;
            let total = 0;
            for (const b of buffers) {
                const n = readSync(this.fd, b, 0, b.length, pos);
                total += n;
                if (pos !== null) pos += n;
                if (n < b.length) break; // short read => EOF
            }
            return total;
        }).then((bytesRead: any) => ({ bytesRead, buffers }));
    }

    /** Writes an array of buffers sequentially; resolves `{ bytesWritten, buffers }`. */
    writev(buffers: any[], position?: any): Promise<any> {
        return __promisifyValue(() => {
            let pos = (position === undefined || position === null) ? null : position;
            let total = 0;
            for (const b of buffers) {
                const n = writeSync(this.fd, b, 0, b.length, pos);
                total += n;
                if (pos !== null) pos += n;
            }
            return total;
        }).then((bytesWritten: any) => ({ bytesWritten, buffers }));
    }

    /** Reads the whole file from the fd; resolves a Buffer (or string with an encoding). */
    readFile(options?: any): Promise<any> {
        return __promisifyValue(() => {
            const size = fstatSync(this.fd).size;
            const buf = Buffer.alloc(size);
            let off = 0;
            while (off < size) {
                const n = readSync(this.fd, buf, off, size - off, null);
                if (n <= 0) break;
                off += n;
            }
            const enc = __encodingOf(options);
            return (enc && enc !== 'buffer') ? buf.toString(enc) : buf;
        });
    }

    /** Writes data to the fd, replacing existing contents. */
    writeFile(data: any, options?: any): Promise<void> {
        return this.write(data).then(() => undefined);
    }

    /** Appends data to the underlying file. */
    appendFile(data: any, options?: any): Promise<void> { return __pAppendFile(this.__path, data, options); }

    /** Retrieves the Stats for the open fd. */
    stat(options?: any): Promise<any> { return __promisifyValue(() => fstatSync(this.fd, options)); }

    /** Truncates the open fd to `len` (default 0). */
    truncate(len?: any): Promise<void> { return __promisifyVoid(() => ftruncateSync(this.fd, len)); }

    /** Changes the permissions of the underlying file. */
    chmod(mode: number): Promise<void> { return __pChmod(this.__path, mode); }

    /** Changes the owner and group of the underlying file. */
    chown(uid: number, gid: number): Promise<void> { return __promisifyVoid(() => chownSync(this.__path, uid, gid)); }

    /** Changes the file-system timestamps of the underlying file. */
    utimes(atime: number, mtime: number): Promise<void> { return __pUtimes(this.__path, atime, mtime); }

    /** Flushes the fd's buffered writes to disk (#976). */
    sync(): Promise<void> { return __promisifyVoid(() => __fsyncSync(this.fd)); }

    /** Flushes the fd's data writes to disk (#976). */
    datasync(): Promise<void> { return __promisifyVoid(() => __fsyncSync(this.fd)); }

    /** Returns a ReadStream over the handle's fd (#980). autoClose defaults false —
     *  the handle owns the descriptor; close it via fh.close(). */
    createReadStream(options?: any): any {
        const o: any = { autoClose: false };
        if (options) for (const k in options) o[k] = options[k];
        o.fd = this.fd;
        return createReadStream(this.__path, o);
    }

    /** Returns a WriteStream over the handle's fd (#980). autoClose defaults false. */
    createWriteStream(options?: any): any {
        const o: any = { autoClose: false };
        if (options) for (const k in options) o[k] = options[k];
        o.fd = this.fd;
        return createWriteStream(this.__path, o);
    }

    /** Closes the file descriptor. Idempotent. */
    close(): Promise<void> {
        return __promisifyVoid(() => {
            if (this.__closed) return;
            this.__closed = true;
            closeSync(this.fd);
        });
    }
}

/** Opens a file and resolves a FileHandle (the fsPromises.open workflow). */
function __openHandle(path: string, flags?: any, mode?: any): Promise<any> {
    return __promisifyValue(() => {
        const f = (flags === undefined || flags === null) ? 'r' : flags;
        const fd = (mode === undefined || mode === null) ? openSync(path, f) : openSync(path, f, mode);
        return new FileHandle(fd, path);
    });
}

// ===========================================================================
// fs.promises namespace — assembled from the promise primitives.
// Each method is wrapped in an arrow so the call reaches the primitive at a
// call-site (the emitter dispatches method calls, not method values).
// ===========================================================================

/** Promise-based file-system API (equivalent to `require('fs/promises')`). */
export const promises = {
    readFile: (path: string, options?: any): Promise<any> => __pReadFile(path, options),
    writeFile: (path: string, data: any, options?: any): Promise<void> => __pWriteFile(path, data, options),
    appendFile: (path: string, data: any, options?: any): Promise<void> => __pAppendFile(path, data, options),
    stat: (path: string, options?: any): Promise<any> => __pStat(path).then((r: any) => __makeStats(r, __statBig(options))),
    lstat: (path: string, options?: any): Promise<any> => __pLstat(path).then((r: any) => __makeStats(r, __statBig(options))),
    unlink: (path: string): Promise<void> => __pUnlink(path),
    mkdir: (path: string, options?: any): Promise<any> => __pMkdir(path, options),
    rmdir: (path: string, options?: any): Promise<void> => __pRmdir(path, options),
    rm: (path: string, options?: any): Promise<void> => __promisifyVoid(() => rmSync(path, options)),
    cp: (src: string, dest: string, options?: any): Promise<void> => __promisifyVoid(() => cpSync(src, dest, options)),
    readdir: (path: string, options?: any): Promise<any> => __wantsFileTypes(options)
        ? (__readdirRecursive(options) ? __pReaddir(path, { recursive: true }) : __pReaddir(path)).then((n: any) => __makeDirents(path, n))
        : __pReaddir(path, options),
    rename: (oldPath: string, newPath: string): Promise<void> => __pRename(oldPath, newPath),
    copyFile: (src: string, dest: string, mode?: any): Promise<void> => __pCopyFile(src, dest, mode),
    access: (path: string, mode?: any): Promise<void> => __pAccess(path, mode),
    chmod: (path: string, mode: number): Promise<void> => __pChmod(path, mode),
    chown: (path: string, uid: number, gid: number): Promise<void> => __promisifyVoid(() => chownSync(path, uid, gid)),
    lchown: (path: string, uid: number, gid: number): Promise<void> => __promisifyVoid(() => lchownSync(path, uid, gid)),
    truncate: (path: string, len?: any): Promise<void> => __pTruncate(path, len),
    utimes: (path: string, atime: number, mtime: number): Promise<void> => __pUtimes(path, atime, mtime),
    readlink: (path: string): Promise<any> => __pReadlink(path),
    realpath: (path: string): Promise<any> => __pRealpath(path),
    symlink: (target: string, path: string, type?: any): Promise<void> => __pSymlink(target, path, type),
    link: (existingPath: string, newPath: string): Promise<void> => __pLink(existingPath, newPath),
    mkdtemp: (prefix: string): Promise<any> => __pMkdtemp(prefix),
    open: (path: string, flags?: any, mode?: any): Promise<any> => __openHandle(path, flags, mode),
    opendir: (path: string, options?: any): Promise<any> => Promise.resolve(opendirSync(path, options)),
    watch: (filename: string, options?: any): any => __watchAsync(filename, options),
    statfs: (path: string, options?: any): Promise<any> => __promisifyValue(() => statfsSync(path, options)),
    glob: (pattern: any, options?: any): any => __arrayAsyncIterator(globSync(pattern, options)),
    constants,
};

// Node's `fs` module exposes its surface as both named exports and a default
// export (the module object). Supports `import fs from 'fs'; fs.readFileSync(...)`.
export default {
    existsSync, readFileSync, writeFileSync, appendFileSync,
    unlinkSync, mkdirSync, rmdirSync, rmSync, cpSync, readdirSync,
    statSync, lstatSync, renameSync, copyFileSync, accessSync,
    chmodSync, chownSync, lchownSync, truncateSync,
    symlinkSync, readlinkSync, realpathSync, utimesSync,
    openSync, closeSync, readSync, writeSync, fstatSync, ftruncateSync,
    fsyncSync, fdatasyncSync, fchmodSync, fchownSync, futimesSync,
    lchmodSync, lutimesSync, readvSync, writevSync, statfsSync, globSync,
    mkdtempSync, opendirSync, linkSync,
    createReadStream, createWriteStream,
    watch, watchFile, unwatchFile,
    constants,
    readFile, writeFile, appendFile, stat, lstat, unlink, mkdir, rmdir,
    rm, cp, readdir, rename, copyFile, access, chmod, truncate, utimes,
    readlink, realpath, symlink, link, mkdtemp,
    chown, lchown, open, close, read, write, fstat, ftruncate,
    fsync, fdatasync, fchmod, fchown, futimes, lchmod, lutimes,
    readv, writev, statfs, glob, exists,
    promises,
};
