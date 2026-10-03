class A {label="parent";value(){return this.label;}}class B extends A {}class C extends B {label="child";read(){return super.value();}}console.log(new C().read());
