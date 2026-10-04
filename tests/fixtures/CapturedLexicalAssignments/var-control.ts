function outer(){var x:any;const set=(v:number)=>{x=v;};set(1);console.log(x);x=2;console.log(x);set(3);console.log(x);}outer();
