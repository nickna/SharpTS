namespace Left {
    export class Box<T> { value = 1; read() { return this.value; } }
}
namespace Right {
    export class Box<T> { value = 2; read() { return this.value; } }
}
class Box { value = 9; read() { return this.value; } }
console.log(new Left.Box<number>().read(), new Right.Box<string>().read(), new Box().read());
