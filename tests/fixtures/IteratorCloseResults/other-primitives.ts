function check(value:any){const it:any={[Symbol.iterator](){return this;},next(){return {value:1,done:false};},return(){return value;}};try{for(const n of it)break;console.log("accepted");}catch(e:any){console.log(e.name);}}
check(null);check(undefined);check(false);check("");check(0n);
