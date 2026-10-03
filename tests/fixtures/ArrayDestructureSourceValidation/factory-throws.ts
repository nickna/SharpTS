const marker={tag:7};const source:any={[Symbol.iterator](){throw marker;}};try{const [a]=source;console.log(a);}catch(e:any){console.log(e===marker);}
