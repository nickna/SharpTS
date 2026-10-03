class A<T> {value(x:T):T{return x;}}class B<T> extends A<T> {read(x:T){return (()=>super.value(x))();}}console.log(new B<string>().read("ok"));
