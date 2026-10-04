class Value{value:number;constructor(n:number){this.value=n;}}
let expectedTarget:any;
const proxy:any=new Proxy(Value,{construct(target:any,args:any[],newTarget:any){
    console.log(target===Value,args.join(','),newTarget===expectedTarget);
    return Reflect.construct(target,args);
}});
expectedTarget=proxy;
console.log(new proxy(8).value);
class Alternate{}
expectedTarget=Alternate;
const reflected:any=Reflect.construct(proxy,[9],Alternate);
console.log(reflected.value);
