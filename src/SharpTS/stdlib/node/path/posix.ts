// node:path/posix aliases the existing path.posix implementation.

import { posix as pathPosix, win32 as pathWin32 } from 'path';

export const resolve = pathPosix.resolve;
export const normalize = pathPosix.normalize;
export const isAbsolute = pathPosix.isAbsolute;
export const join = pathPosix.join;
export const relative = pathPosix.relative;
export const dirname = pathPosix.dirname;
export const basename = pathPosix.basename;
export const extname = pathPosix.extname;
export const format = pathPosix.format;
export const parse = pathPosix.parse;
export const sep = pathPosix.sep;
export const delimiter = pathPosix.delimiter;
export const posix = pathPosix;
export const win32 = pathWin32;

export default pathPosix;
