class A<T> {value(x:T):T{return x;}}class B<T> extends A<T> {async read(x:T){await Promise.resolve(0);return super.value(x);}}new B<string>().read("ok").then(v=>console.log(v));
