import {Readable} from "node:stream";async function run(){const stream:any=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}await run();
