class Box<T> { static value = 'present'; }
const constructor: any = Box;
console.log(constructor.value);
const descriptor = Object.getOwnPropertyDescriptor(constructor, 'value');
console.log(descriptor === undefined ? 'missing' : descriptor.value);
