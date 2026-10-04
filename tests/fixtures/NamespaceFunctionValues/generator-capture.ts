namespace Sequence {const base=4;export function* values(){yield base;yield base+1;}}const sequence:any=Sequence;console.log([...Sequence.values()].join(","),[...sequence.values()].join(","));
