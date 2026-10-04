function factory(){let outer=0;const make=()=>{let inner=10;return async()=>++outer + ++inner;};return make();}
async function run(){const a=factory();const b=factory();console.log(await a(),await a(),await b(),await a());}run();
