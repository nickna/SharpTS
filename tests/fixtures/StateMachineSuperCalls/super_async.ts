class A {value(x:number){return x+2;}} class B extends A {async read(){const a=super.value(3);await Promise.resolve(0);return a+super.value(4);}} new B().read().then(v=>console.log(v));
