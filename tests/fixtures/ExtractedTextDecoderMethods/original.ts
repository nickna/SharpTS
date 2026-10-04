const d:any=new TextDecoder();const decode:any=d.decode;console.log(decode.call(d,new Uint8Array([65])));
