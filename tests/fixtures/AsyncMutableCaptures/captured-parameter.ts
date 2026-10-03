function factory(n:number){return async()=>++n;}
async function run(){const a=factory(0);const b=factory(10);console.log(await a(),await a(),await b(),await a(),await b());}run();
