class A {value(x:number=9){return x;}}class B extends A {read(){return (()=>super.value())();}}console.log(new B().read());
