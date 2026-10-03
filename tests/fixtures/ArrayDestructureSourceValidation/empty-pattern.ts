for(const source of [null,undefined,4,{}] as any[]){try{const []=source;console.log("accepted");}catch(e:any){console.log(e.name);}}
