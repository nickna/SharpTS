function* values(){yield* "a😀";}const result:any=[...values()];console.log(result.length,result[1].length,result[1].charCodeAt(0));
