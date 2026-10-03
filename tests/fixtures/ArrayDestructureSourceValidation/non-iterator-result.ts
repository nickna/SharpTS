for(const result of [{},[]] as any[]){const source:any={[Symbol.iterator](){return result;}};try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
