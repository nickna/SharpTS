const retained:any[]=[];
const o:any={
    f:function(a:any,b:any){retained.push(arguments);return a*10+b;},
    one:function(a:any){return a;},
    three:function(a:any,b:any,c:any){return a*100+b*10+c;}
};
console.log(o.f(o.f(1,2),o.f(3,4)));
console.log(retained[0][0],retained[0][1],retained[1][0],retained[1][1],retained[2][0],retained[2][1]);
console.log(retained[0]!==retained[1],retained[1]!==retained[2]);
console.log(o.f(o.one(12),o.three(0,3,4)));
console.log(o.f(o.f(o.f(1,2),o.f(3,4)),o.f(5,6)));
