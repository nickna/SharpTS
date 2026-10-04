namespace Left {
    export class Box { value = 1; read() { return this.value; } }
}
namespace Right {
    export class Box { value = 2; read() { return this.value; } }
}
class Box { value = 9; read() { return this.value; } }
console.log(new Left.Box().read(), new Right.Box().read(), new Box().read());
