class A<T> {value(x:T):T{return x;}}class B<U> extends A<U> {}class C extends B<number> {read():number{return super.value(3);}}
