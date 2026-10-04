function reject(fn:any){try{new fn();return false;}catch(e:any){return e.name==='TypeError'&&e instanceof TypeError;}}
const arrow:any=()=>1;
const asyncArrow:any=async()=>1;
async function asyncFn(){}
function* generator(){}
async function* asyncGenerator(){}
const functions:any[]=[arrow,asyncArrow,asyncFn,generator,asyncGenerator];
for(const fn of functions){
    const bound:any=fn.bind({});
    const again:any=bound.bind({});
    console.log(reject(fn),reject(bound),reject(again));
}
function Value(this:any){this.value=7;}
const C:any=Value;const first:any=C.bind({});const second:any=first.bind({});
console.log(new C().value,new first().value,new second().value);
