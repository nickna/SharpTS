// node:path/win32 aliases the existing path.win32 implementation.

import { posix as pathPosix, win32 as pathWin32 } from 'path';

export const resolve = pathWin32.resolve;
export const normalize = pathWin32.normalize;
export const isAbsolute = pathWin32.isAbsolute;
export const join = pathWin32.join;
export const relative = pathWin32.relative;
export const dirname = pathWin32.dirname;
export const basename = pathWin32.basename;
export const extname = pathWin32.extname;
export const format = pathWin32.format;
export const parse = pathWin32.parse;
export const sep = pathWin32.sep;
export const delimiter = pathWin32.delimiter;
export const posix = pathPosix;
export const win32 = pathWin32;

export default pathWin32;
