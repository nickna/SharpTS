const first:any={tag:8};const second:any={tag:9};
async function* values(){try{yield 1;}catch(e){console.log(e===first);yield 2;}finally{console.log("closed");}}
async function run(){const g=values();console.log((await g.next()).value);const caught=await g.throw(first);console.log(caught.value,caught.done);try{await g.throw(second);}catch(e){console.log(e===second);}const last=await g.next();console.log(last.value,last.done);}run();
