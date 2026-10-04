function outer(){const get=()=>x;try{get();}catch(e){console.log(e.name);}let x:any=undefined;console.log(get()===undefined);x=4;console.log(get());}outer();
