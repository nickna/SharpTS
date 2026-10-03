function* values(){yield Promise.resolve(2);yield Promise.resolve(4);}
async function run(){let total=0;for await(const value of values()){const n:number=value;total+=n;}console.log(total);}run();
