function read(){return 99;}namespace Outer {export namespace Inner {const base=4;function read(){return base+1;}export const result=read();}}console.log(Outer.Inner.result,read());
