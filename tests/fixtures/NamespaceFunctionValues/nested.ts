namespace Outer.Inner {export const value=7;export function read(){return value;}}const outer:any=Outer;console.log(Outer.Inner.value,outer.Inner.read(),outer.Inner===Outer.Inner);
