class A {value(){return 1;}}class B extends A {}class C extends B {read(){return super.missing();}}
