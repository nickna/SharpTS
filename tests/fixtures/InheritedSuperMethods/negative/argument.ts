class A {value(x:number){return x;}}class B extends A {}class C extends B {read(){return super.value('bad');}}
