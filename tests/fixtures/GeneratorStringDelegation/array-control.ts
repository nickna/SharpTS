function* values(){yield* [1,2];}console.log([...values()].join(","));
