function optional(a:number,b?:number):string{return 'value'+(a+(b??0));}
function rest(a:number,...values:number[]):string{return 'count'+values.length;}
const one:[number]=[2];
const many:[number,number,number]=[2,3,4];
const first:string=Reflect.apply(optional,undefined,one);
const second:string=Reflect.apply(rest,undefined,many);
console.log(first,second);
