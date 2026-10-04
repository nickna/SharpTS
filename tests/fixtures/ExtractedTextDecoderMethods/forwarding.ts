const d:any=new TextDecoder();
const other:any=new TextDecoder();
const decode:any=d.decode;
const bytes:any=new Uint8Array([88,65,66,89]);
const pair:any=bytes.subarray(1,3);
const single:any=bytes.subarray(2,3);
console.log(decode.call(other,pair));
console.log(decode.apply(other,[single]));
const storage:any=new ArrayBuffer(4);
const view:any=new Uint8Array(storage);
view[0]=88;view[1]=65;view[2]=66;view[3]=89;
const words:any=new Uint16Array(storage,0,2);
console.log(decode.call(other,words));
console.log(decode.call(other).length,decode.call(other,new Uint8Array(0)).length);
for(const receiver of [null,undefined,{},[]]){
    try{decode.call(receiver,bytes);console.log(false);}catch(error){console.log(error instanceof TypeError);}
}
