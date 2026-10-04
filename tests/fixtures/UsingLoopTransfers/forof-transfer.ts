for(const value of [0,1,2]){using r={value,[Symbol.dispose](){console.log(this.value);}};if(value===0)continue;break;}console.log("after");
