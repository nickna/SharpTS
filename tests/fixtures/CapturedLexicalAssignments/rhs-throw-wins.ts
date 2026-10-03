function outer(){function rhs():number{throw new Error("rhs");}const set=()=>{x=rhs();};try{set();}catch(e){console.log(e.name,e.message);}let x=2;console.log(x);}outer();
