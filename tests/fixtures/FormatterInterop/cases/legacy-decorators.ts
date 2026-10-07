// formatter-comment: this file deliberately uses CRLF and legacy parameter decorators.
function mark(target:any):void{console.log("class:legacy");}
function method(target:any,key:string,descriptor:any):void{console.log("method:"+key);}
function parameter(target:any,key:string|null,index:number):void{console.log("parameter:"+index+":"+key);}
@mark class Greeter{@method greet(@parameter name:string):void{console.log("Hello, "+name);}}
new Greeter().greet("formatter");
