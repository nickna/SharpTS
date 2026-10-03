function f(this:any,a:number,b:number){return this.x+a+b;}function run(){const c:any=f.call;const a:any=f.apply;console.log(c.call(f,{x:1},2,3),a.call(f,{x:4},[5,6]));}run();
