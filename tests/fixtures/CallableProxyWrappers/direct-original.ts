const p:any=new Proxy(function(a:number){return a;},{apply(target:any,receiver:any,args:any[]){return receiver.x+args[0];}});console.log(p.call({x:1},2),p.apply({x:4},[5]),p.bind({x:7},8)());
