// New investigation fixture for #1903, not a recovered historical source.
class Base {
    #field = 1;
    #method() { return this.#field; }
    static has(value: any) {
        console.log(#field in value, #method in value);
    }
}
class Derived extends Base { #field = 2; #method() { return this.#field; } }
class Other { #field = 3; #method() { return this.#field; } }
const value = new Base();
Base.has(value);
Base.has(new Derived());
Base.has(new Other());
Base.has({ field: 1, method: () => 1 });
Base.has(new Proxy(value, {}));
