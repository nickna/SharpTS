class A {value(x:number){return x+2;}}class B extends A {read(){return [1,2].map(x=>{const method=super.value;return method(x);}).join(",");}}console.log(new B().read());
