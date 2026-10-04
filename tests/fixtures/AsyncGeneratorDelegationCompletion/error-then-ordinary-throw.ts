const first:any={tag:8};const second:any={tag:9};function* inner(){yield 1;throw first;}
async function* outer(){try{yield* inner();}catch(e){console.log(e===first);yield 2;}finally{console.log("outer");}}
async function run(){const g=outer();console.log((await g.next()).value);console.log((await g.next()).value);try{await g.throw(second);}catch(e){console.log(e===second);}console.log((await g.next()).done);}run();
