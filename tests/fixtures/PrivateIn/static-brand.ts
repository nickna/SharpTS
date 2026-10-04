// New investigation fixture for #1903, not a recovered historical source.
class Box<T> {
    static #field = 1;
    static #method() { return Box.#field; }
    static has(value: any) {
        console.log(#field in value, #method in value);
    }
}
class Derived extends Box<number> {}
const alias = Box;
Box.has(Box);
Box.has(alias);
Box.has(Derived);
Box.has(new Box<string>());
Box.has(function () {});
