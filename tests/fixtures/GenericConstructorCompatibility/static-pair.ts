// Negative #1905 control: constructor-side field types differ.
class Box<T> { static value: number = 1; }
class Other<U> { static value: string = "bad"; }
const Alias: typeof Box = Other;
