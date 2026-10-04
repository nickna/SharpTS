namespace Values {const base=6;async function read(){await Promise.resolve(0);return base+1;}export const result=read();}Values.result.then(value=>console.log(value));
