async function accept(items:Array<Promise<number>|string>):Promise<void>{for await(const value of items){const result:number=value;}}
