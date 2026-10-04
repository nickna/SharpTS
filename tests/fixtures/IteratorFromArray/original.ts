const a:any=[1,2];const wrapped:any=Iterator.from(a);console.log(wrapped===a,Iterator.from(wrapped)===wrapped,wrapped.next().value);
