const source:any=[1,2,3];const [a,...rest]=source;console.log(a,rest.join(","));const [...copy]=source;console.log(copy===source,copy.join(","));
