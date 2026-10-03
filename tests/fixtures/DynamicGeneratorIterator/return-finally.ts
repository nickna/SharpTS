function* values(){try{yield 1;yield 2;}finally{console.log("finally");}}
const g:any=values(); const method:any=g[Symbol.iterator];
console.log(method.apply(g,[])===g,method.bind(g)()===g);
const first=g.next(); console.log(first.value,first.done);
const result=g.return(7); console.log(result.value,result.done);
const last=g.next(); console.log(last.value,last.done,g[Symbol.iterator]()===g);
