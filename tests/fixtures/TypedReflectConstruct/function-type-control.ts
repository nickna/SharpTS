class Point{x:number;constructor(x:number){this.x=x;}}
function identity<T extends Function>(constructor:T):T{return constructor;}
const broad:Function=Point;
const signature:new (x:number)=>Point=Point;
const a:any=Reflect.construct(identity(Point),[9]);
const b:any=Reflect.construct(broad,[11]);
const c:any=Reflect.construct(signature,[13]);
console.log(a.x,a instanceof Point,b.x,b instanceof Point,c.x,c instanceof Point);
