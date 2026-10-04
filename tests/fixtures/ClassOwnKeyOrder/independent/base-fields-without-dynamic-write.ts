class Base{a:number=2;}class Child extends Base{b:number=3;}const c:any=new Child();c.extra=9;console.log(Object.keys(c).join(","));
