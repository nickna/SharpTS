function outer(){const set=(v:number)=>x=v;let x=set(1);console.log(x);}try{outer();}catch(e){console.log(e.name);}try{outer();}catch(e){console.log(e.name);}
