import { Counter } from "dotnet:NativeInteropExample.Counter";

const counter = new Counter(40);
counter.addEventListener("Changed", () => console.log("changed"));
console.log(counter.increment(2), counter.value, counter.label);

const counters = Counter.createMany(3);
console.log(counters.count);
