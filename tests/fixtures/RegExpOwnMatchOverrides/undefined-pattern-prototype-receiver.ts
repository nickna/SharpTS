RegExp.prototype[Symbol.match]=function(s:any):any{console.log(this instanceof RegExp);return [typeof s,s];};console.log("abc".match(undefined)!.join("|"));
