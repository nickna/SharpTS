const value:any=/b/;value[Symbol.match]=function(s:any){console.log(this===value,s);return "hook";};console.log("abc".match(value));delete value[Symbol.match];console.log("abc".match(value)![0]);
