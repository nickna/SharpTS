function Value(){}
const invalidHandler:any={construct(){return 1;}};
const invalid:any=new Proxy(Value,invalidHandler);
try{new invalid();console.log(false);}catch(error){console.log(error instanceof TypeError);}
const token:any={failure:true};
const failing:any=new Proxy(Value,{construct(){throw token;}});
try{new failing();console.log(false);}catch(error){console.log(error===token);}
const valid:any=new Proxy(Value,{construct(){return {value:9};}});
console.log(new valid().value);
