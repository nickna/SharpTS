const r:any={ [Symbol.dispose](){console.log("first");} };{using x=r;r[Symbol.dispose]=function(){console.log("replacement");};console.log("body");}
