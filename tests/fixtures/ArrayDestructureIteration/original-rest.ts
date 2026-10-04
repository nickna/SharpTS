const source:any={i:0,[Symbol.iterator](){return this;},next(){this.i++;return {value:this.i*3,done:this.i>3};}};const [a,...rest]=source;console.log(a,rest.join(","),source.i);
