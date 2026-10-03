async function run(){let total=0;for await(const value of [Promise.resolve(2),4,Promise.resolve(3)]){const n:number=value;total+=n;}console.log(total);}run();
