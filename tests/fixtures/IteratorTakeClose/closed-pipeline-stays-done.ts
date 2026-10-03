const mapped=Iterator.from([1,2,3]).map((n:any)=>n*2);console.log(mapped.take(1).toArray().join(","),mapped.next().done);
const filtered=Iterator.from([1,2,3]).filter((n:any)=>n>0);console.log(filtered.take(1).toArray().join(","),filtered.next().done);
const dropped=Iterator.from([1,2,3]).drop(1);console.log(dropped.take(1).toArray().join(","),dropped.next().done);
const flattened=Iterator.from([1,2]).flatMap((n:any)=>[n,n+1]);console.log(flattened.take(1).toArray().join(","),flattened.next().done);
