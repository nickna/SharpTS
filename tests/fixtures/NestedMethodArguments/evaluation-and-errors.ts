const events:any[]=[];
const o:any={f:function(a:any,b:any){events.push('invoke');return a*10+b;}};
const target:any={get f(){events.push('get');return o.f;}};
function receiver(){events.push('receiver');return target;}
function left(){events.push('left');return o.f(1,2);}
const value:any={get right(){events.push('right');return o.f(3,4);}};
console.log(receiver().f(left(),value.right));
console.log(events.join(','));
const token:any={failure:true};
function fail(){events.push('throw');throw token;}
events.length=0;
try{receiver().f(left(),fail());}catch(error){console.log(error===token);}
console.log(events.join(','));
console.log(o.f(5,6));
