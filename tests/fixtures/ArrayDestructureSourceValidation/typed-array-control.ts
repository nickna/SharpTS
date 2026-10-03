const source=new Uint8Array([1,2,3]);const [a,...rest]=source;console.log(a,Array.isArray(rest),rest.join(","));
