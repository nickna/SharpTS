const r:any={value:5};Object.defineProperty(r,Symbol.dispose,{get:function(){console.log("get");return function(){console.log(this.value);};}});{using x=r;console.log("body");}
