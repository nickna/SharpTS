// Negative #1905 control: constructed instance method results differ.
class Box<T> { read(): number { return 1; } }
class Other<U> { read(): string { return "bad"; } }
const Alias: typeof Box = Other;
