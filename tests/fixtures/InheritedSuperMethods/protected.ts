class A {protected value(x:number){return x+2;}}class B extends A {}class C extends B {read(){return super.value(3);}}console.log(new C().read());
