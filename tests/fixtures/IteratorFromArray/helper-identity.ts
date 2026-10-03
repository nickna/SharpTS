const source:any=Iterator.from([1,2]).map((n:any)=>n*3);const wrapped=Iterator.from(source);console.log(wrapped===source,wrapped.next().value,wrapped.toArray().join(","));
