Array.prototype[Symbol.iterator]=function*(){yield 8;yield 9;};const source:any=[1,2];const [a,...rest]=source;console.log(a,rest.join(","));
