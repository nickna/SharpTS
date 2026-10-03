async function run(){const items:ReadonlyArray<Promise<number>|number> = [Promise.resolve(2),4];let total=0;for await(const value of items){const n:number=value;total+=n;}console.log(total);}run();
