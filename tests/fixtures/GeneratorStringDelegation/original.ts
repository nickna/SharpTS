function* values(){yield* [1,2];yield* "a😀";}console.log([...values()].join(","));
