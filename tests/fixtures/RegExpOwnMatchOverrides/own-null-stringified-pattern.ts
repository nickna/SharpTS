const value:any=/b/g;value[Symbol.match]=null;value.lastIndex=2;console.log("abc".match(value)===null);const result:any="/b/g".match(value);console.log(result[0],result.index,value.lastIndex);
