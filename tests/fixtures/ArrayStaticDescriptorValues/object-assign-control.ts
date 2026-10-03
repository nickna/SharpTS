const O:any=Object;
const key="assign";
const first=Object.getOwnPropertyDescriptor(O,key)!;
const second=Object.getOwnPropertyDescriptor(Object,key)!;
console.log(first.value===second.value,first.value===O[key],first.value===Object.assign);
console.log(first.value.name,first.value.length,first.writable,first.enumerable,first.configurable);
const target:any={a:1};
const result=first.value(target,{b:2});
console.log(result===target,result.a,result.b);
