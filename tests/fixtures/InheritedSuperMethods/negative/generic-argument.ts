class A<T> {value(x:T):T{return x;}}class B extends A<number> {}class C extends B {read(){return super.value('bad');}}
