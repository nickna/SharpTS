enum E{A=2,B=3}async function read(key:number):Promise<string>{await Promise.resolve(1);return E[key];}read(3).then(value=>console.log(value));
