// Strict assertion-mode alias for node:assert/strict.

import {
    AssertionError,
    CallTracker,
    deepStrictEqual,
    doesNotMatch,
    doesNotReject,
    doesNotThrow,
    fail,
    ifError,
    match,
    notDeepStrictEqual,
    notStrictEqual,
    ok,
    partialDeepStrictEqual,
    rejects,
    strict as strictAssert,
    strictEqual,
    throws,
} from 'assert';

export {
    AssertionError,
    CallTracker,
    deepStrictEqual,
    doesNotMatch,
    doesNotReject,
    doesNotThrow,
    fail,
    ifError,
    match,
    notDeepStrictEqual,
    notStrictEqual,
    ok,
    partialDeepStrictEqual,
    rejects,
    strictEqual,
    throws,
};

export const equal = strictEqual;
export const notEqual = notStrictEqual;
export const deepEqual = deepStrictEqual;
export const notDeepEqual = notDeepStrictEqual;
export const strict = strictAssert;

export default strictAssert;
