function sum(a:number,b:number){return a+b;}const proxy:any=new Proxy(sum,{apply(t:any,r:any,args:any[]){return (Reflect.apply(t,r,args) as number)+1;}});console.log(typeof proxy,proxy(2,3));
