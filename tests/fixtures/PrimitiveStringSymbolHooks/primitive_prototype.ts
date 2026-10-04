(Number.prototype as any)[Symbol.search]=function(s:string){return s.length+4;};console.log("abc".search(1 as any));
