const marker={tag:7};let calls=0;try{Iterator.from([2,3,4]).forEach((n:any,i:any)=>{calls++;console.log(n,i);throw marker;});console.log("accepted");}catch(e:any){console.log(e===marker,calls);}
