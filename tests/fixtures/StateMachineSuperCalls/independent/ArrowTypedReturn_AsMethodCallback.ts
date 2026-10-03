class Calculator {
    value: number = 0;

    apply(fn: (x: number) => number): void {
        this.value = fn(this.value);
    }
}
let calc = new Calculator();
calc.value = 5;
calc.apply((x: number): number => x * 2);
console.log(calc.value);