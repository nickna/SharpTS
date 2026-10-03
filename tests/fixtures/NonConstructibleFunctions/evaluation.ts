let effects=0;
function argument(){effects++;return 1;}
const token:any={failure:true};
const arrow:any=()=>{throw token;};
try{new arrow(argument());}catch(e){console.log(e instanceof TypeError,effects);}
function Ordinary(this:any){this.value=2;}
const C:any=Ordinary;console.log(new C().value,effects);
