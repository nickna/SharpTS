const o:any={f:function(a:any,b:any){return a*10+b;}};
const value:any={valueOf(){return o.f(3,4);}};
console.log(o.f(o.f(1,2),+value));
