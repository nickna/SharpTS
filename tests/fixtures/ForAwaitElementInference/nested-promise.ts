async function accept(items:Iterable<Promise<Promise<number>>>):Promise<void>{for await(const value of items){const result:number=value;}}
console.log("accepted");
