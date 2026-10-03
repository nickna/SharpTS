class Base{a:number=2;}class Child extends Base{b:number=3;}const c:any=new Child();c["a"]=5;c["b"]=7;console.log(c.a,c.b,"a" in c,"b" in c,Object.keys(c).join(","));
