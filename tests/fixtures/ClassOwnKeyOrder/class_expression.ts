const Box=class{value:number=2;read(){return this.value;}};const b:any=new Box();b["value"]=6;console.log(b.value,b.read(),"value" in b,"absent" in b,Object.keys(b).join(","));
