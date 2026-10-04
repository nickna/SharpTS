function factory(){let n=0;return {async inc(){return ++n;},async read(){return n;},async add(value:number){await Promise.resolve(0);n+=value;return n;}};}
async function run(){const value=factory();console.log(await value.inc(),await value.read(),await value.add(5),await value.inc(),await value.read());}run();
