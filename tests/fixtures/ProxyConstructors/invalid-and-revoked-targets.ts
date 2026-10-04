let calls=0;
const handler:any={construct(){calls++;return {};}};
const arrow:any=()=>1;
for(const target of [{},arrow]){
    const proxy:any=new Proxy(target,handler);
    try{new proxy();console.log(false);}catch(error){console.log(error instanceof TypeError);}
}
console.log(calls);
function Value(){}
const pair:any=Proxy.revocable(Value,handler);
pair.revoke();
try{new pair.proxy();console.log(false);}catch(error){console.log(error instanceof TypeError);}
const alias:any=Proxy;
for(const target of [null,undefined,1]){
    try{new alias(target,{});console.log(false);}catch(error){console.log(error instanceof TypeError);}
}
