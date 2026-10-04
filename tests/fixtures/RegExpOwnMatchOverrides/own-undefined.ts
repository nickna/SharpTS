const value:any=/b/;value[Symbol.match]=undefined;console.log("abc".match(value)===null);const result:any="/b/".match(value);console.log(result[0],result.index,result.input);
