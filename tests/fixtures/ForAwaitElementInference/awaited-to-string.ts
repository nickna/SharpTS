async function accept(items:Promise<number>[]):Promise<void>{for await(const value of items){const result:string=value;}}
