async function accept(items:Iterable<Promise<number>|number>):Promise<void>{for await(const value of items){const result:number=value;}}
console.log("accepted");
