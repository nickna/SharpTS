function label(...pair:[number,number]):string{return 'value'+(pair[0]+pair[1]);}
const args:[number,number]=[2,3];
const result:string=Reflect.apply(label,undefined,args);
console.log(result);
