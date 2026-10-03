class Value{value:number;constructor(a:number,b:number){this.value=a*10+b;}}
const proxy:any=new Proxy(Value,{});
const nested:any=new Proxy(proxy,{});
const bound:any=proxy.bind(null,2);
const first:any=new proxy(2,3);
const second:any=new nested(4,5);
const third:any=new bound(6);
console.log(first.value,second.value,third.value);
console.log(first instanceof Value,second instanceof Value,third instanceof Value);
console.log(Object.getPrototypeOf(first)===Value.prototype,Object.getPrototypeOf(second)===Value.prototype);
