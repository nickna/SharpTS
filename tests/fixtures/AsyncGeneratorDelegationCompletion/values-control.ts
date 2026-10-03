async function* inner(){yield 2;yield 3;}async function* outer(){yield* inner();}async function run(){let total=0;for await(const n of outer())total+=n;console.log(total);}run();
