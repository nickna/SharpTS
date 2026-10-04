const a:any=[1];const wrapped=Iterator.from(a);console.log(wrapped.next().value);a.push(2,3);console.log(wrapped.next().value,wrapped.toArray().join(","));
