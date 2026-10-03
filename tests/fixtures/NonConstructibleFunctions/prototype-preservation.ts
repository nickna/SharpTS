function* generator(){}
async function* asyncGenerator(){}
async function asyncFn(){}
const G:any=generator;const AG:any=asyncGenerator;const AF:any=asyncFn;
const arrow:any=()=>1;
console.log(G.prototype!==undefined,AG.prototype!==undefined,arrow.prototype===undefined,AF.prototype===undefined);
