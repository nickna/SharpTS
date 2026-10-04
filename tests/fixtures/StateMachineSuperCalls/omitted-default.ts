class A {value(x:number=9){return x;}}class B extends A {*read(){yield super.value();}}console.log(new B().read().next().value);
