const a:any=[1,2];const wrapped:any=Iterator.from(a);console.log("before-next");try{const result=wrapped.next();console.log("next",result.value);}catch(e){console.log("caught",e.name,e.message);}
