async function* values(){try{const sent:any=yield 1;yield sent;}finally{console.log("closed");}return 0;}
async function run(){const g=values();console.log((await g.next()).value);const sent=await g.next(7);console.log(sent.value,sent.done);const last=await g.return(9);console.log(last.value,last.done);console.log((await g.next()).done);}run();
