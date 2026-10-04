const other:any=[5,6];other[Symbol.iterator]=function*(){yield 9;};const source:any=[1,2,3];const [a,...rest]=source;console.log(a,rest.join(","));
