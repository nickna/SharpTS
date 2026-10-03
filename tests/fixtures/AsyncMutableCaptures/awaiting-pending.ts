function factory(){let n=0;return {async next(){await new Promise<number>(resolve=>setTimeout(()=>resolve(0),1));n+=2;return n;}};}
async function run(){const value=factory();console.log(await value.next(),await value.next(),await value.next());}run();
