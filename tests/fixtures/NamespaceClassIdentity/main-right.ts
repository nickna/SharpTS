import { Library as Right } from './right';
import { Library as Left } from './left';
console.log(new Left.Box<number>().read(), new Right.Box<string>().read());
