const marker={tag:7};const a:any=[1];Object.defineProperty(a,"0",{get(){throw marker;}});const wrapped=Iterator.from(a);try{wrapped.next();}catch(e:any){console.log(e===marker);}
