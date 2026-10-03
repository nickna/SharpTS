for(const limit of ["2",true,null] as any[]){console.log(Iterator.from([1,2,3]).take(limit).toArray().join(","),Iterator.from([1,2,3]).drop(limit).toArray().join(","));}
