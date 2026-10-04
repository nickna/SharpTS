const other=[3,4];other[Symbol.iterator]=function*(){yield 8;yield 9;};const source=[1,2];source[Symbol.iterator]=other[Symbol.iterator];const [a,...rest]=source;console.log(a,rest.join(","));
