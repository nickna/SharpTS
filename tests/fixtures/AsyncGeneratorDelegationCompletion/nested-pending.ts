async function* inner(){yield await new Promise<number>(resolve=>setTimeout(()=>resolve(3),1));return 4;}
async function* middle(){return yield* inner();}async function* outer(){const value:any=yield* middle();return value+1;}
async function run(){const g=outer();const a=await g.next();const b=await g.next();console.log(a.value,a.done,b.value,b.done);}run();
