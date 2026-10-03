const holder:any={evaluate:eval};const text=String("1+2");try{console.log(holder.evaluate(text));}catch(e:any){console.log(String(e.message).includes("SharpTS runtime not present"));}
