import{StringBuilder as Builder}from "dotnet:System.Text.StringBuilder";
import{InteropProbe}from "dotnet:SharpTS.FormatterInteropEvidence.InteropProbe";
// formatter-comment: neither import scheme nor aliases are formatter syntax extensions.
console.log(InteropProbe.join("dotnet","reference"));
const builder:Builder=new Builder();builder.append("formatter ").append(42);
console.log(builder.toString());
