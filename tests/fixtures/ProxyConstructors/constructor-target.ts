class Value{value:number;constructor(n:number){this.value=n;}}const ProxyValue:any=new Proxy(Value,{});console.log(new ProxyValue(8).value);
