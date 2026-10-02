function Splitter(pattern: any, flags: any): any {
 console.log(pattern.source, flags); return new RegExp('b', flags);
}
const r: any = /a/; r.constructor = { [Symbol.species]: Splitter };
const parts: any = RegExp.prototype[Symbol.split].call(r, 'abc', 2);
console.log(parts.join('|'), r.lastIndex);