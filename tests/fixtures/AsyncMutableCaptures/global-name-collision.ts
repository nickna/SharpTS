let n=100;function factory(){let n=0;return async()=>++n;}
async function run(){const a=factory();console.log(await a(),await a(),n);}run();
