class A {label="parent";value(){return this.label;}}class B extends A {label="child";read(){return (()=>super.value())();}}console.log(new B().read());
