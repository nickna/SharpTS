enum Before { A=2, B=A<<1, All=A|B }
console.log(Before.All, Before[6]);
await Promise.resolve(0);
enum After { A=3, B=A+1 }
const alias:any=After;
console.log(alias===After, alias[4], After.B);
export {};
