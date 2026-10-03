const token:any={failure:true};
function Failed(this:any){this.value=1;throw token;}
const receiver:any={value:90};
const bound:any=Failed.bind(receiver);
try{new bound();console.log(false);}catch(error){console.log(error===token,receiver.value);}
function Later(this:any){this.value=2;}
const next:any=Later.bind(receiver);
console.log(new next().value,receiver.value);
