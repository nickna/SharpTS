const source=[1,2];const alias:any=source;alias[Symbol.iterator]=function*(){yield 8;yield 9;};const [a,...rest]=source;console.log(a,rest.join(","));
