class Point{x:number;constructor(x:number){this.x=x;}}const p:any=Reflect.construct(Point as any,[7]);console.log(p.x,p instanceof Point);for(const value of [null,undefined,{},Function.prototype.call]){try{Reflect.construct(value as any,[]);console.log(false);}catch(error){console.log(error instanceof TypeError);}}

