function Matcher(pattern: any, flags: any): any {
 console.log(pattern.source, flags); return new RegExp('b', flags);
}
const r: any = /a/g; r.lastIndex = 1; r.constructor = { [Symbol.species]: Matcher };
const iterator: any = RegExp.prototype[Symbol.matchAll].call(r, 'abb');
const a: any = iterator.next(); const b: any = iterator.next(); const end: any = iterator.next();
console.log(a.value[0], a.value.index, b.value[0], b.value.index, end.done, r.lastIndex);