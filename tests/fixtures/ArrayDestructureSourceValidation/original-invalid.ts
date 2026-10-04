for(const source of [null,undefined,4,{}]){try{const [a]=source as any;console.log("accepted",a);}catch(e){console.log(e.name);}}
