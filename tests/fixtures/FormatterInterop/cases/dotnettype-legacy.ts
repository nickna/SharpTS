@DotNetType("System.Text.StringBuilder")
declare class StringBuilder{constructor();append(value:string):StringBuilder;toString():string;}
const builder:StringBuilder=new StringBuilder();builder.append("dotnettype");
console.log(builder.toString());
