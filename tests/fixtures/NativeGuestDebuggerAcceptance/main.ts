import { addTwo } from "./helper";
const seed = Number(process.argv[2]); // @break:entry
const answer = addTwo(seed); // @step:call
console.log(`native-debug=${answer}`); // @break:output
