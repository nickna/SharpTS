let x="outer";function outer(){const get=()=>()=>x;try{get()();}catch(e){console.log(e.name);}let x="inner";console.log(get()());}outer();console.log(x);
