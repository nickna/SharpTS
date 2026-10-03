for(const source of [false,1n,Symbol("value"),()=>1] as any[]){try{const [a]=source;console.log("accepted",a);}catch(e:any){console.log(e.name);}}
