let x=9;function outer(){const set=()=>{x=1;};try{set();}catch(e){console.log(e.name);}let x=2;console.log(x);}outer();console.log(x);
