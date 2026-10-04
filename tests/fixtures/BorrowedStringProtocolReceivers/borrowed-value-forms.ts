const value:any={[Symbol.match](s:any){return typeof s+":"+s;}};const match:any=String.prototype.match;console.log(match.call(77,value),match.apply(77,[value]),match.bind(77,value)());
