function factory(start:number){let n=start;return {async next(){await Promise.resolve(0);return ++n;},current(){return n;},reset(value:number){n=value;}};}
async function run(){const a=factory(0);const b=factory(10);console.log(await a.next(),await a.next(),await b.next());console.log(a.current(),b.current());a.reset(20);console.log(await a.next(),b.current());}run();
