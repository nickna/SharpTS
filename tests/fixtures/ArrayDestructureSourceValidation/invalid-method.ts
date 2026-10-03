for(const method of [null,undefined,4] as any[]){const source:any={};source[Symbol.iterator]=method;try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
