async function accept(items:Promise<string>[]):Promise<void>{for await(const value of items){const result:number=value;}}
