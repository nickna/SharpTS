// node:util/types aliases the maintained util.types predicate namespace.

import { types } from 'util';

export const isArray = types.isArray;
export const isDate = types.isDate;
export const isFunction = types.isFunction;
export const isNull = types.isNull;
export const isUndefined = types.isUndefined;
export const isPromise = types.isPromise;
export const isRegExp = types.isRegExp;
export const isMap = types.isMap;
export const isSet = types.isSet;
export const isTypedArray = types.isTypedArray;
export const isNativeError = types.isNativeError;
export const isBoxedPrimitive = types.isBoxedPrimitive;
export const isWeakMap = types.isWeakMap;
export const isWeakSet = types.isWeakSet;
export const isArrayBuffer = types.isArrayBuffer;
export const isSharedArrayBuffer = types.isSharedArrayBuffer;
export const isAnyArrayBuffer = types.isAnyArrayBuffer;
export const isDataView = types.isDataView;
export const isArrayBufferView = types.isArrayBufferView;
export const isBigIntObject = types.isBigIntObject;
export const isBooleanObject = types.isBooleanObject;
export const isNumberObject = types.isNumberObject;
export const isStringObject = types.isStringObject;
export const isSymbolObject = types.isSymbolObject;
export const isProxy = types.isProxy;
export const isExternal = types.isExternal;
export const isModuleNamespaceObject = types.isModuleNamespaceObject;
export const isKeyObject = types.isKeyObject;
export const isCryptoKey = types.isCryptoKey;
export const isGeneratorFunction = types.isGeneratorFunction;
export const isAsyncFunction = types.isAsyncFunction;
export const isGeneratorObject = types.isGeneratorObject;
export const isInt8Array = types.isInt8Array;
export const isUint8Array = types.isUint8Array;
export const isUint8ClampedArray = types.isUint8ClampedArray;
export const isInt16Array = types.isInt16Array;
export const isUint16Array = types.isUint16Array;
export const isInt32Array = types.isInt32Array;
export const isUint32Array = types.isUint32Array;
export const isFloat32Array = types.isFloat32Array;
export const isFloat64Array = types.isFloat64Array;
export const isBigInt64Array = types.isBigInt64Array;
export const isBigUint64Array = types.isBigUint64Array;

export default types;
