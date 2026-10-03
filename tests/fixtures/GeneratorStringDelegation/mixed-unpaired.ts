function* values(){yield* "a😀b\uD800c\uDC00𝄞";}
const result:any=[...values()];
console.log(result.length);
console.log(result.map((value:any)=>value.length).join(","));
console.log(result.map((value:any)=>value.charCodeAt(0)).join(","));
