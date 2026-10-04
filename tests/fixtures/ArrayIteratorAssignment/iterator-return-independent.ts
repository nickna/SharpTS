const source=[1,2];const alias:any=source;alias[Symbol.iterator]=()=>[8,9].values();const [a,...rest]=source;console.log(a,rest.join(","));
