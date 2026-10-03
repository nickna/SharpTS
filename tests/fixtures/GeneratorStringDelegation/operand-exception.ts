const error:any={label:"boom"}; function text():string{throw error;}
function* values(){try{yield* text();}catch(e){console.log(e===error);}finally{console.log("finally");}return 8;}
const g:any=values(); const last=g.next();console.log(last.value,last.done);
