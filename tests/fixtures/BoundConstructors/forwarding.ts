function Value(this:any,a:any,b:any){this.value=a*10+b;return 0;}
const receiver:any={value:99};
const other:any={value:88};
const first:any=Value.bind(receiver,2);
const second:any=first.bind(other,3);
const instance:any=new second();
console.log(instance.value,receiver.value,other.value,instance===receiver);
console.log(instance instanceof Value,Object.getPrototypeOf(instance)===Value.prototype);
first(4);console.log(receiver.value,other.value);
const object:any={value:7};
function Returned(this:any){this.value=1;return object;}
const returns:any=Returned.bind(receiver);
console.log(new returns()===object,receiver.value);
