for(const method of [null,4,false]){const source:any=[1,2];source[Symbol.iterator]=method;try{const [...values]=source;console.log("accepted");}catch(e){console.log(e.name);}}
