const source:[number,number]=[1,2];const alias:any=source;alias[Symbol.iterator]=function*(){yield 8;yield 9;};const dynamic:any=source;const [a,...rest]=dynamic;console.log(a,rest.join(","));
