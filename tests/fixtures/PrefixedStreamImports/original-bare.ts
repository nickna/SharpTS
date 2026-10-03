import {Readable} from "stream";async function run(){const stream=Readable.from([1,2,3]);let total=0;for await(const n of stream)total+=n;console.log(total);}run();
