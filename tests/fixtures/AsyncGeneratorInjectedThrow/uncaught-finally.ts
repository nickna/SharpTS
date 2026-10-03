const error:any={tag:8};
async function* values(){try{yield 1;}finally{console.log("closed");}}
async function run(){const g=values();console.log((await g.next()).value);try{await g.throw(error);}catch(e){console.log(e===error);}const last=await g.next();console.log(last.value,last.done);}run();
