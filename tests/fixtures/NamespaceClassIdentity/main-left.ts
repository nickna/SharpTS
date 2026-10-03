import { Library as Left } from './left';
import { Library as Right } from './right';
console.log(new Left.Box<number>().read(), new Right.Box<string>().read());
