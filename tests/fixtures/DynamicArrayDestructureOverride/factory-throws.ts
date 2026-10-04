const source:any=[1,2];const marker={};source[Symbol.iterator]=()=>{throw marker;};try{const [...values]=source;console.log("accepted");}catch(e){console.log(e===marker);}
