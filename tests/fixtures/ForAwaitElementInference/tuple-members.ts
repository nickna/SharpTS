async function accept(items:Map<string,Promise<number>>):Promise<void>{for await(const value of items){const key:string=value[0];const result:Promise<number> = value[1];}}
console.log("accepted");
