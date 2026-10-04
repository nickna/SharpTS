class Base{a:number=2;}class Child extends Base{b:number=3;}const c:any=new Child();c.a=5;c.extra=9;c.b=7;console.log(Object.keys(c).join(","),Object.values(c).join(","));
