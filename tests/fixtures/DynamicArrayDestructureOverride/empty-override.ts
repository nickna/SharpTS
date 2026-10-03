const source:any=[1,2];source[Symbol.iterator]=function*(){};const [a=7,...rest]=source;console.log(a,rest.length);
