function f(this:any,a:number,b:number){return this.x+a+b;}
function g(this:any,a:number,b:number){return this.x*10+a+b;}
const c:any=f.call; const a:any=f.apply; const b:any=f.bind;
console.log(c.apply(f,[{x:2},3,4]),a.apply(f,[{x:5},[6,7]]));
console.log(c.call(g,{x:1},2,3),a.call(g,{x:4},[5,6]));
const boundCall:any=c.bind(f,{x:3},4);
console.log(boundCall(5),boundCall.call(g,6));
const boundApply:any=a.bind(g,{x:2},[3,4]);
console.log(boundApply());
console.log(b.call(g,{x:4},5)(6));
console.log(f.bind({x:7},8).bind({x:99},9)());
