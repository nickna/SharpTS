const o:any={f:function(a:any,b:any){return a*10+b;}};
console.log(o.f(o.f(1,2),Array.from([0],()=>o.f(3,4))[0]));
