// Negative #1905 control: instance-private origins differ.
class Box<T> { private value: number = 1; }
class Other<U> { private value: number = 1; }
const Alias: typeof Box = Other;
