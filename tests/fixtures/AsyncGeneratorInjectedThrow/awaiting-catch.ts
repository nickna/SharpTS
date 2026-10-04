const error:any={tag:8};
async function* values(){try{yield 1;}catch(e){console.log(e===error);await Promise.resolve(0);yield 2;}finally{await Promise.resolve(0);console.log("closed");}return 9;}
async function run(){const g=values();console.log((await g.next()).value);const caught=await g.throw(error);console.log(caught.value,caught.done);const last=await g.next();console.log(last.value,last.done);}run();
