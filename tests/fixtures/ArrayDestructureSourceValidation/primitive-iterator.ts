const source:any={[Symbol.iterator](){return 4;}};try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}
