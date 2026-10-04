function outer(){const make=()=>()=>{x=3;};const set=make();try{set();}catch(e){console.log(e.name);}let x=2;set();console.log(x);}outer();
