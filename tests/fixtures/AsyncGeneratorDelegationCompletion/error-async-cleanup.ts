const error:any={tag:8};async function* inner(){try{yield 1;throw error;}finally{console.log("inner");}}
async function* outer(){try{yield* inner();}catch(e){console.log(e===error);}finally{console.log("outer");}return 9;}
async function run(){const g=outer();console.log((await g.next()).value);const last=await g.next();console.log(last.value,last.done);}run();
