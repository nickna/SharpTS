const value:any=/b/;value[Symbol.match]=null;const match:any=String.prototype.match;console.log(match.call("abc",value)===null,match.apply("/b/",[value])[0],match.bind("/b/",value)()[0]);
