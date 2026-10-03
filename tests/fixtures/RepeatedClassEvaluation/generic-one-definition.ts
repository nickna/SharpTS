// Unchanged source from GenericComputedFieldKeys_AreSharedAcrossTypeArguments.
let counter = 0;
class Box<T> { [counter++] = 5; }
console.log(counter);
const numberBox: any = new Box<number>();
const stringBox: any = new Box<string>();
console.log(counter, numberBox[0], stringBox[0], stringBox[1]);
