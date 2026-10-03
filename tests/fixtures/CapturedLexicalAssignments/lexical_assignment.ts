function outer(){const set=(v:number)=>{x=v;};try{set(1);}catch(e){console.log(e.name);}let x=2;console.log(x);set(3);console.log(x);}outer();
