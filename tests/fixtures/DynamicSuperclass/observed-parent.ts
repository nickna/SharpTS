// New #1907 investigation control; the historical source was not recovered.
class Left {
    static side = "left";
    value = 1;
    constructor() { console.log("left"); }
    read() { return this.value; }
}
class Right {
    static side = "right";
    value = 2;
    constructor() { console.log("right"); }
    read() { return this.value; }
}
async function make(parent: any) {
    const selected = await Promise.resolve(parent);
    return class extends selected {};
}
async function main() {
    const Child: any = await make(Right);
    const value: any = new Child();
    console.log(typeof value.read, Child.side);
    console.log(Object.getPrototypeOf(Child) === Right);
    console.log(value instanceof Right, value instanceof Left);
}
main().then(() => {}, error => console.log("rejected", error.message));
