const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return Symbol("close");}};try{const [value]=it;console.log(value);}catch(e:any){console.log(e.name);}
