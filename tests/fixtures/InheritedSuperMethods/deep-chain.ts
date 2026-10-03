class A {value(x:number){return x+2;}}class B extends A {}class C extends B {}class D extends C {read(){return super.value(3);}}console.log(new D().read());
