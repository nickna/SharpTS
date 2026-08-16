import { StringBuilder } from "dotnet:System.Text";
import { List } from "dotnet:System.Collections.Generic.List<number>";

const text = new StringBuilder().append("native-dotnet-");
const values = new List();
values.add(40);
values.add(42);
console.log(text.append(values[1]).toString());
