const source=[1,2];const key=Symbol.iterator;source[key]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
