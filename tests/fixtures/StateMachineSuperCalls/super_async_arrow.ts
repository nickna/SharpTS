class A {value(x:number){return x+2;}} class B extends A {read(){const f=async()=>{await Promise.resolve(0);return super.value(5);};return f();}} new B().read().then(v=>console.log(v));
