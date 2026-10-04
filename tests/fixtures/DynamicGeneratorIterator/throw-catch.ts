const error:any={label:"boom"};
function* values(){try{yield 1;}catch(e){console.log(e===error);yield 2;}finally{console.log("finally");}return 9;}
const g:any=values(); console.log(g[Symbol.iterator]()===g);
console.log(g.next().value);
const caught=g.throw(error); console.log(caught.value,caught.done);
const last=g.next(); console.log(last.value,last.done);
