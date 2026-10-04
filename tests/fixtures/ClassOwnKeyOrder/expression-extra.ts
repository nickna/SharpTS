const Box=class{value:number=2;other:string="text";};const b:any=new Box();b.extra=9;b.value=7;console.log(Object.keys(b).join(","),Object.values(b).join(","));
