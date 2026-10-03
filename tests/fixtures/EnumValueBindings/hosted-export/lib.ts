let calls=0;
function next():number { calls++; return 3; }
export enum Values { A=next(), B=A+1 }
console.log(Values.B);
await Promise.resolve(0);
console.log(calls);
export const alias=Values;
