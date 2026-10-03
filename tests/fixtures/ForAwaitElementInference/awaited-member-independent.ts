async function accept():Promise<void>{for await(const value of [Promise.resolve(2)]){value.then((n:number)=>n);}}
