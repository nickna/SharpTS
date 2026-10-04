class A<T> {value(x:T):T{return x;}}class B extends A<number> {}class C extends B {read():number{return super.value(3);}}console.log(new C().read());
