async function accept(items:ReadonlyArray<Promise<number>>):Promise<void>{for await(const value of items){const result:Promise<number> = value;}}
