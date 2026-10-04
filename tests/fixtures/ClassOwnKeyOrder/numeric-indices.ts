class Box{z:number=1;a:number=2;}const b:any=new Box();b["10"]=10;b.extra=3;b["2"]=2;b["01"]=1;console.log(Object.keys(b).join(","));console.log(Object.getOwnPropertyNames(b).join(","));
