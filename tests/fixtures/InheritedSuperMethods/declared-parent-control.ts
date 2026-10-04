class A {value(){return "grandparent";}}class B extends A {value(){return super.value();}}class C extends B {read(){return super.value();}}console.log(new C().read());
