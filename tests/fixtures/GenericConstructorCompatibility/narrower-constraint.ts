// Negative #1905 control: the source cannot construct every target type argument.
class Box<T> { constructor(value: T) {} }
class Other<U extends string> { constructor(value: U) {} }
const Alias: typeof Box = Other;
