async function accept(items:AsyncIterable<Promise<number>>):Promise<void>{for await(const value of items){const result:Promise<number> = value;}}
