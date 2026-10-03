function* sync(){if(false)yield 0;return 8;}async function* asynchronous(){if(false)yield 0;return await Promise.resolve(9);}
async function* outer(){const a:any=yield* sync();const b:any=yield* asynchronous();const c:any=yield* [];console.log(a,b,c===undefined);return 10;}
async function run(){const last=await outer().next();console.log(last.value,last.done);}run();
