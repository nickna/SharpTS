class A {value(x:number){return x+2;}}class B extends A {read(){return (()=>()=>()=>{const method=super.value;return method(3);})()()();}}console.log(new B().read());
