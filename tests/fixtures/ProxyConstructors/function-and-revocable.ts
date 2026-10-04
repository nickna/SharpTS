function Value(this:any,a:number,b:number){this.value=a*10+b;}
const proxy:any=new Proxy(Value,{});
const first:any=new proxy(2,3);
console.log(first.value,first instanceof Value,Object.getPrototypeOf(first)===Value.prototype);
class Box{value:number;constructor(n:number){this.value=n;}}
const pair:any=Proxy.revocable(Box,{});
const second:any=new pair.proxy(7);
console.log(second.value,second instanceof Box);
pair.revoke();
try{new pair.proxy(8);console.log(false);}catch(error){console.log(error instanceof TypeError);}
