function targetRejected(fn:any){try{Reflect.construct(fn,[]);return false;}catch(e){return e instanceof TypeError;}}
function newTargetRejected(fn:any){try{Reflect.construct(function(){},[],fn);return false;}catch(e){return e instanceof TypeError;}}
const functions:any[]=[()=>1,async function(){},function*(){},async function*(){}];
for(const fn of functions){
    const bound:any=fn.bind({});
    console.log(targetRejected(fn),newTargetRejected(fn),targetRejected(bound),newTargetRejected(bound));
}
