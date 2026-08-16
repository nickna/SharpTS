// SharpTS Example Library
// Demonstrates comprehensive TypeScript features for C# interop
// NOTE: Async functions require runtime loading - see AsyncLibrary.ts

// ============================================
// 1. Basic Class with Properties and Methods
// ============================================
class Person {
    name: string;
    age: number;

    constructor(name: string, age: number) {
        this.name = name;
        this.age = age;
    }

    greet(): string {
        return "Hello, my name is " + this.name + " and I am " + this.age + " years old.";
    }

    haveBirthday(): void {
        this.age = this.age + 1;
    }
}

// ============================================
// 2. Static Members and Instance Methods
// ============================================
class Calculator {
    static PI: number = 3.14159;

    static add(a: number, b: number): number {
        return a + b;
    }

    static multiply(a: number, b: number): number {
        return a * b;
    }

    accumulator: number;

    constructor() {
        this.accumulator = 0;
    }

    addToAccumulator(value: number): number {
        this.accumulator = this.accumulator + value;
        return this.accumulator;
    }

    reset(): void {
        this.accumulator = 0;
    }
}

// ============================================
// 3. Inheritance
// ============================================
class Animal {
    name: string;

    constructor(name: string) {
        this.name = name;
    }

    speak(): string {
        return this.name + " makes a sound";
    }
}

class Dog extends Animal {
    breed: string;

    constructor(name: string, breed: string) {
        super(name);
        this.breed = breed;
    }

    speak(): string {
        return this.name + " barks!";
    }

    getInfo(): string {
        return this.name + " is a " + this.breed;
    }
}

// ============================================
// 4. Top-level Function
// ============================================
function formatMessage(prefix: string, message: string): string {
    return "[" + prefix + "] " + message;
}

// ============================================
// 5. Arrays and Objects
// ============================================
class DataProcessor {
    items: string[];

    constructor() {
        this.items = [];
    }

    addItem(item: string): void {
        this.items.push(item);
    }

    getCount(): number {
        return this.items.length;
    }

    getItemAt(index: number): string {
        return this.items[index];
    }

    getAllItems(): string[] {
        return this.items;
    }
}
