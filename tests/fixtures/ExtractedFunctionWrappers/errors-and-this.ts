const token:any={tag:'same'};
function boom(){throw token;}
const c:any=boom.call; const a:any=boom.apply; const b:any=boom.bind;
try{c.call(boom,null);}catch(error){console.log(error===token);}
try{a.apply(boom,[null,[]]);}catch(error){console.log(error===token);}
try{b.call(boom,null)();}catch(error){console.log(error===token);}
function strict(this:any){'use strict';return this;}
const sc:any=strict.call; const sa:any=strict.apply;
console.log(sc.call(strict,null)===null,sa.call(strict,undefined,[])===undefined);
