function attempt(fn:any){try{new fn();return false;}catch(e){return e instanceof TypeError;}}console.log(attempt((x:number)=>x),attempt(async function(){}),attempt(function*(){}));
