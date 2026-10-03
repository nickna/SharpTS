function work():number{for(let i=0;i<3;i++){{using r={value:i,[Symbol.dispose](){console.log(this.value);}};if(i===0)continue;return 7;}}return 8;}console.log(work());
