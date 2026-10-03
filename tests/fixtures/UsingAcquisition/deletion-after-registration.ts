const r:any={value:5,[Symbol.dispose](){console.log(this===r,this.value);}};{using x=r;delete r[Symbol.dispose];r.value=9;console.log("body");}
