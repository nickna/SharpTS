function* inner(){const empty:any=yield* "";console.log(empty===undefined);const result:any=yield* "😀";console.log(result===undefined);return 9;}
function* outer(){const result:any=yield* inner();return result;}
const g:any=outer(); const first=g.next();console.log(first.value.length,first.done);
const last=g.next(7);console.log(last.value,last.done);
