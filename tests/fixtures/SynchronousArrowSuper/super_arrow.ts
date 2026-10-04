class A {value(x:number){return x+2;}} class B extends A {read(){const outer=()=>()=>super.value(3);return outer()();}} console.log(new B().read());
