function f(this:any,a:number,b:number){return this.x+a+b;}
const p:any=new Proxy(f,{apply(target:any,receiver:any,args:any[]){return target.apply(receiver,args)*2;}});
console.log('call',p.call({x:1},2,3));
console.log('apply',p.apply({x:4},[5,6]));
try{console.log('bind',p.bind({x:7},8)(9));}catch(error){console.log('bind rejected',error instanceof TypeError);}
