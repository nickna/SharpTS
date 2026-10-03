// New #1905 boundary control: alpha-renaming preserves an equal constraint.
class Box<T extends string> {
    value: T;
    constructor(value: T) { this.value = value; }
}
class Other<U extends string> {
    value: U;
    constructor(value: U) { this.value = value; }
}
const Alias: typeof Box = Other;
console.log(new Alias("ok").value);
