const original:any=new Uint8Array([1,2,3,4]);
const other:any=new Uint8Array([5,6,7,8]);
const fill:any=original.fill;
console.log(fill.call(other,9,1,3)===other);
console.log(original.join(','),other.join(','));
console.log(fill.apply(other,[4,0,1])===other,other.join(','));
const floating:any=new Float64Array([1.5,2.5]);
console.log(fill.call(floating,3.5)===floating,floating[0],floating[1]);
for(const receiver of [null,undefined,{},[]]){
    try{fill.call(receiver,0);console.log(false);}catch(error){console.log(error instanceof TypeError);}
}
