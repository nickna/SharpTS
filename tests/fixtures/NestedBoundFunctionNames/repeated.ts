function sample(a:number,b:number,c:number){return a+b+c;}
const first:any=sample.bind(null,1);
const second:any=first.bind({ignored:1},2);
const third:any=second.bind({ignored:2},3);
console.log(first.name,second.name,third.name);
console.log(first.length,second.length,third.length,third());
first.tag=7;second.tag=8;
console.log(first.tag,second.tag,third.tag===undefined,first.missing===undefined);
const fourth:any=third.bind(null,4);
console.log(fourth.name,fourth.length,fourth(),fourth.tag===undefined);
