const root:any=globalThis;const g:any=root["isNaN"];console.log(g.call({},"x"),g.apply(null,["4"]),g.bind({},"x")());console.log(root.isNaN("x"),globalThis.isNaN("4" as any));
