async function* inner(){try{const sent:any=yield 1;yield sent;return 9;}finally{await Promise.resolve(0);console.log("inner");}}
async function* outer(){try{const result:any=yield* inner();yield result+1;}finally{console.log("outer");}}
async function run(){const g=outer();const a=await g.next();console.log(a.value,a.done);const b=await g.next(7);console.log(b.value,b.done);const c=await g.next();console.log(c.value,c.done);console.log((await g.next()).done);}run();
