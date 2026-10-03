const error:any={tag:8};
async function* values(){try{try{yield 1;}finally{console.log("inner");}}catch(e){console.log(e===error);yield 2;}finally{console.log("outer");}}
async function run(){const g=values();console.log((await g.next()).value);const caught=await g.throw(error);console.log(caught.value,caught.done);console.log((await g.next()).done);}run();
