function* inner(value:any){yield 1;return value;}async function* outer(value:any){return yield* inner(value);}
async function run(){const object:any={tag:8};for(const value of [null,undefined,false,0,"",object]){const g=outer(value);await g.next();const last=await g.next();console.log(last.value===value,last.done);}}run();
