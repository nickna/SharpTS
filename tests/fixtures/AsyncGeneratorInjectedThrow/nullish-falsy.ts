async function* values(){try{yield 1;}catch(e){yield e;}}
async function run(){for(const error of [null,undefined,false,0,""]){const g=values();await g.next();const caught=await g.throw(error);console.log(caught.value===error,caught.done);console.log((await g.next()).done);}}run();
