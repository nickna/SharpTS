class Box{first:number=((this as any).early=8,1);second:number=2;}const b:any=new Box();b.later=9;console.log(Object.keys(b).join(","));
