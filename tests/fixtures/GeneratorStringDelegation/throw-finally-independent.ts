function* values(){try{yield* "😀ab";}finally{console.log("finally");}}
const g:any=values(); const first=g.next();console.log(first.value.length,first.done);
try{g.throw("boom");}catch(e){console.log(e instanceof TypeError);}
const after=g.next();console.log(after.value,after.done);
