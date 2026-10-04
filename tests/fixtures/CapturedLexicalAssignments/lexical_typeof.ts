function outer(){const get=()=>typeof x;try{get();}catch(e){console.log(e.name);}let x:any=undefined;console.log(get());}outer();
