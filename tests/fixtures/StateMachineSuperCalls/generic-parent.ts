class A<T> {value(x:T):T{return x;}}class B extends A<number> {async read(){await Promise.resolve(0);return super.value(3);}}new B().read().then(v=>console.log(v));
