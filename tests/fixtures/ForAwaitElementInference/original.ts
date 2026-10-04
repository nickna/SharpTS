async function run(){let total=0;for await(const n of [Promise.resolve(2),Promise.resolve(4)])total+=n;console.log(total);}run();
