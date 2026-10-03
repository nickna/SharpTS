class A {value(x:number){return x+2;}}class B extends A {}class C extends B {async read(){await Promise.resolve(0);return super.value(3);}}new C().read().then(v=>console.log(v));
