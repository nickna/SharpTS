function label(a:number,b:number):string{return 'ok';}
const args:[number,number]=[2,3];
const invalid:number=Reflect.apply(label,undefined,args);
