for(const limit of [2147483648,4294967296,Number.MAX_VALUE,Infinity]){console.log(Iterator.from([1,2,3]).take(limit).toArray().join(","),Iterator.from([1,2,3]).drop(limit).toArray().length);}
