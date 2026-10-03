function label(a:number,b:number):string{return 'value'+(a+b);}
const args:readonly [number,number]=[2,3];
const inferred:string=Reflect.apply(label,undefined,args);
const explicit:string=Reflect.apply<undefined,[number,number],string>(label,undefined,args);
console.log(inferred,explicit);
