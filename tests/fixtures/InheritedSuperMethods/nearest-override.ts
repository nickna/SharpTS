class A {value(){return 1;}}class B extends A {value(){return 2;}}class C extends B {}class D extends C {read(){return super.value();}}console.log(new D().read());
