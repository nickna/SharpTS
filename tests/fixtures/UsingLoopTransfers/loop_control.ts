for(let i=0;i<3;i++){using r={value:i,[Symbol.dispose](){console.log(this.value);}};if(i===0)continue;if(i===1)break;}console.log("after");
