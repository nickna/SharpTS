async function* values(){if(false)yield 0;return Promise.resolve(9);}
async function run(){const last=await values().next();console.log(last.value,last.done);}run();
