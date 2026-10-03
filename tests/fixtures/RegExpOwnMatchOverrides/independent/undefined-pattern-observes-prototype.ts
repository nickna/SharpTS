RegExp.prototype[Symbol.match]=function(s:any):any{return [this.source,s];};console.log("abc".match(undefined)!.join("|"));
