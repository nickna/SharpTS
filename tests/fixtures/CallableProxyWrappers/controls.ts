function f(this:any,a:number,b:number){return this.x+a+b;}
const direct:any=f;
console.log(direct.call({x:1},2,3),direct.apply({x:4},[5,6]),direct.bind({x:7},8)(9));
const inner:any=new Proxy(f,{apply(target:any,receiver:any,args:any[]){return target.apply(receiver,args)*2;}});
const outer:any=new Proxy(inner,{apply(target:any,receiver:any,args:any[]){return target.apply(receiver,args)+1;}});
console.log(outer.call({x:1},2,3),outer.apply({x:4},[5,6]),outer.bind({x:7},8)(9));
const marker:any={same:true};
const throwing:any=new Proxy(f,{apply(){throw marker;}});
try{throwing.call(null,1,2);}catch(error){console.log(error===marker);}
try{throwing.apply(null,[1,2]);}catch(error){console.log(error===marker);}
try{throwing.bind(null,1)(2);}catch(error){console.log(error===marker);}
const bind:any=f.bind;
try{bind.call(new Proxy({},{}),null);console.log('accepted');}catch(error){console.log(error instanceof TypeError);}
