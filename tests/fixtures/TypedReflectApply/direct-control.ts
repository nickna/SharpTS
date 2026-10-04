const proxy:any=new Proxy(function(a:number,b:number){return a+b;},{apply(t:any,r:any,args:any[]){return args[0]+args[1]+1;}});console.log(typeof proxy,proxy(2,3));
