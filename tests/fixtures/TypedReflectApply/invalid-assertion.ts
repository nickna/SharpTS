function label(a:number,b:number):string{return 'ok';}
const args:[number,number]=[2,3];
console.log(Reflect.apply(label,undefined,args) as number);
