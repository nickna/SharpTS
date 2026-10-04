function* values(){yield 1;}const g:any=values();const method:any=g[Symbol.iterator];console.log(typeof method);
