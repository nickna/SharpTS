function logged(value:any,context:any):void{console.log(context.kind+":"+context.name);}
@logged class Decorated{value():string{return "stage3";}}
console.log(new Decorated().value());
