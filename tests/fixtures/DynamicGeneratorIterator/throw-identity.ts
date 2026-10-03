function* values(){try{yield 1;}finally{console.log("finally");}}
const error:any={label:"boom"}; const g:any=values();
const method:any=g[Symbol.iterator]; console.log(method.call(g)===g,g.next().value);
try{g.throw(error);}catch(e){console.log(e===error);}
const last=g.next(); console.log(last.value,last.done,method.call(g)===g);
