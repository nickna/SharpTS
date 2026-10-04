const source=["a","b"];source[Symbol.iterator]=function*(){yield "x";yield "y";};const [a,...rest]=source;console.log(a,rest.join(","));
