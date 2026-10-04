async function run(){const values:any=[Promise.resolve(2),Promise.resolve(4)];let total=0;for await(const n of values)total+=n;console.log(total);}run();
