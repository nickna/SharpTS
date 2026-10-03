class A {value(x:number){return x+2;}}class B extends A {read(){const f=()=>{const method=super.value;return method(3);};return f();}}console.log(new B().read());
