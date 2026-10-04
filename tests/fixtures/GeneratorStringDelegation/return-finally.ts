function* values(){try{yield* "😀ab";}finally{console.log("finally");}}
const g:any=values(); const first=g.next();console.log(first.value.length,first.done);
const last=g.return(5);console.log(last.value,last.done);
const after=g.next();console.log(after.value,after.done);
