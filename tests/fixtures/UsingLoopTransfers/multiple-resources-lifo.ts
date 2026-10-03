function make(value:number):any{return {value,[Symbol.dispose](){console.log(this.value);}};}for(let i=0;i<3;i++){using a=make(i*10),b=make(i*10+1);if(i===0)continue;break;}console.log("after");
