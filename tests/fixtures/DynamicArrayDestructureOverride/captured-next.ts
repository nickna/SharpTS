const source:any=[1,2];let gets=0;source[Symbol.iterator]=()=>{let n=0;return {get next(){gets++;return ()=>({value:++n*4,done:n>2});}};};const [a,...rest]=source;console.log(a,rest.join(","),gets);
