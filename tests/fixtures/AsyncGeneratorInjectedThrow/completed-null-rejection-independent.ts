const error:any={tag:8};async function* values(){console.log("body");yield 1;}
async function run(){const g=values();try{await g.throw(error);}catch(e){console.log(e===error);}const last=await g.next();console.log(last.value,last.done);try{await g.throw(null);}catch(e){console.log(e===null);}}run();
