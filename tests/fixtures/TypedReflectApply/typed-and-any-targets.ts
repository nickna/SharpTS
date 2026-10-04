function sum(a:number,b:number){return a+b;}
const args:[number,number]=[2,3];
const typed:number=Reflect.apply(sum,undefined,args);
const dynamic:any=sum;
const asserted:number=Reflect.apply(dynamic,undefined,args) as number;
console.log(typed,asserted);
