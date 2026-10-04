class A {sum(a:number,b:number){return a+b;}}class B extends A {*read(){return super.sum(1,yield 2);}}const g=new B().read();console.log(g.next().value,g.next(3).value);
