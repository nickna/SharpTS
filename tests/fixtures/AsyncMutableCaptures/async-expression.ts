function factory(){let n=0;const next=async function(){await Promise.resolve(0);return ++n;};return {next,read:()=>n};}
async function run(){const value=factory();console.log(await value.next(),value.read(),await value.next(),value.read());}run();
