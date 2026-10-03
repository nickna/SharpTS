const it:any={[Symbol.iterator](){return this;},next(){return {value:3,done:false};},return(){return 3;}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}
