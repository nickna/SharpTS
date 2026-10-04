const value:any=/b/;for(const method of [false,0,17,"x",{}]){value[Symbol.match]=method;try{"abc".match(value);}catch(e){console.log(e.name,e instanceof TypeError);}}
