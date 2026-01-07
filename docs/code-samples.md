# Code Samples: TypeScript to C#

This document shows TypeScript code samples alongside their conceptual C# equivalents. Use this to understand what SharpTS can do and how TypeScript constructs map to .NET.

> **Note:** The C# code shown is *conceptual* - it represents what the compiled code does, not the literal IL output. Actual compiled assemblies use runtime wrappers for dynamic behavior.

---

## Table of Contents

1. [Basic Types and Variables](#basic-types-and-variables)
2. [Functions](#functions)
3. [Classes](#classes)
4. [Interfaces](#interfaces)
5. [Enums](#enums)
6. [Arrays and Collections](#arrays-and-collections)
7. [Control Flow](#control-flow)
8. [Advanced Features](#advanced-features)
9. [Modules](#modules)
10. [Decorators](#decorators)
11. [Type Mapping Reference](#type-mapping-reference)

---

## Basic Types and Variables

### Primitive Types

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
let count: number = 42;
let name: string = "Alice";
let active: boolean = true;
let nothing: null = null;
```

</td>
<td>

```csharp
double count = 42;
string name = "Alice";
bool active = true;
object nothing = null;
```

</td>
</tr>
</table>

### Type Inference

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
let x = 10;           // inferred as number
let s = "hello";      // inferred as string
let flag = false;     // inferred as boolean
```

</td>
<td>

```csharp
var x = 10.0;         // double
var s = "hello";      // string
var flag = false;     // bool
```

</td>
</tr>
</table>

### Constants

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const PI: number = 3.14159;
const GREETING: string = "Hello";
```

</td>
<td>

```csharp
const double PI = 3.14159;
const string GREETING = "Hello";
```

</td>
</tr>
</table>

---

## Functions

### Basic Functions

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function add(a: number, b: number): number {
    return a + b;
}

function greet(name: string): void {
    console.log("Hello, " + name);
}
```

</td>
<td>

```csharp
double Add(double a, double b)
{
    return a + b;
}

void Greet(string name)
{
    Console.WriteLine("Hello, " + name);
}
```

</td>
</tr>
</table>

### Arrow Functions

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const square = (x: number): number => x * x;

const multiply = (a: number, b: number): number => {
    return a * b;
};
```

</td>
<td>

```csharp
Func<double, double> square = x => x * x;

Func<double, double, double> multiply = (a, b) =>
{
    return a * b;
};
```

</td>
</tr>
</table>

### Default Parameters

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function greet(
    name: string,
    greeting: string = "Hello"
): string {
    return greeting + ", " + name;
}

greet("Alice");           // "Hello, Alice"
greet("Bob", "Hi");       // "Hi, Bob"
```

</td>
<td>

```csharp
string Greet(
    string name,
    string greeting = "Hello")
{
    return greeting + ", " + name;
}

Greet("Alice");           // "Hello, Alice"
Greet("Bob", "Hi");       // "Hi, Bob"
```

</td>
</tr>
</table>

### Rest Parameters

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function sum(...numbers: number[]): number {
    let total: number = 0;
    for (let n of numbers) {
        total = total + n;
    }
    return total;
}

sum(1, 2, 3, 4);  // 10
```

</td>
<td>

```csharp
double Sum(params double[] numbers)
{
    double total = 0;
    foreach (var n in numbers)
    {
        total = total + n;
    }
    return total;
}

Sum(1, 2, 3, 4);  // 10
```

</td>
</tr>
</table>

### Closures

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function makeCounter(): () => number {
    let count: number = 0;
    return (): number => {
        count = count + 1;
        return count;
    };
}

const counter = makeCounter();
counter();  // 1
counter();  // 2
```

</td>
<td>

```csharp
Func<double> MakeCounter()
{
    double count = 0;
    return () =>
    {
        count = count + 1;
        return count;
    };
}

var counter = MakeCounter();
counter();  // 1
counter();  // 2
```

</td>
</tr>
</table>

---

## Classes

### Basic Class

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
class Person {
    name: string;
    age: number;

    constructor(name: string, age: number) {
        this.name = name;
        this.age = age;
    }

    greet(): string {
        return "Hello, I'm " + this.name;
    }
}

const alice = new Person("Alice", 30);
alice.greet();  // "Hello, I'm Alice"
```

</td>
<td>

```csharp
class Person
{
    public string Name { get; set; }
    public double Age { get; set; }

    public Person(string name, double age)
    {
        Name = name;
        Age = age;
    }

    public string Greet()
    {
        return "Hello, I'm " + Name;
    }
}

var alice = new Person("Alice", 30);
alice.Greet();  // "Hello, I'm Alice"
```

</td>
</tr>
</table>

### Inheritance

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
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
        return this.name + " barks";
    }
}

const rex = new Dog("Rex", "German Shepherd");
rex.speak();  // "Rex barks"
```

</td>
<td>

```csharp
class Animal
{
    public string Name { get; set; }

    public Animal(string name)
    {
        Name = name;
    }

    public virtual string Speak()
    {
        return Name + " makes a sound";
    }
}

class Dog : Animal
{
    public string Breed { get; set; }

    public Dog(string name, string breed)
        : base(name)
    {
        Breed = breed;
    }

    public override string Speak()
    {
        return Name + " barks";
    }
}

var rex = new Dog("Rex", "German Shepherd");
rex.Speak();  // "Rex barks"
```

</td>
</tr>
</table>

### Getters and Setters

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
class Circle {
    private _radius: number;

    constructor(radius: number) {
        this._radius = radius;
    }

    get radius(): number {
        return this._radius;
    }

    set radius(value: number) {
        if (value > 0) {
            this._radius = value;
        }
    }

    get area(): number {
        return Math.PI * this._radius * this._radius;
    }
}

const c = new Circle(5);
c.radius;      // 5
c.area;        // ~78.54
c.radius = 10;
```

</td>
<td>

```csharp
class Circle
{
    private double _radius;

    public Circle(double radius)
    {
        _radius = radius;
    }

    public double Radius
    {
        get => _radius;
        set
        {
            if (value > 0)
            {
                _radius = value;
            }
        }
    }

    public double Area => Math.PI * _radius * _radius;
}

var c = new Circle(5);
c.Radius;      // 5
c.Area;        // ~78.54
c.Radius = 10;
```

</td>
</tr>
</table>

### Static Members

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
class MathUtils {
    static PI: number = 3.14159;

    static square(x: number): number {
        return x * x;
    }
}

MathUtils.PI;          // 3.14159
MathUtils.square(4);   // 16
```

</td>
<td>

```csharp
class MathUtils
{
    public static double PI = 3.14159;

    public static double Square(double x)
    {
        return x * x;
    }
}

MathUtils.PI;          // 3.14159
MathUtils.Square(4);   // 16
```

</td>
</tr>
</table>

### Abstract Classes

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
abstract class Shape {
    abstract area(): number;

    describe(): string {
        return "A shape with area " + this.area();
    }
}

class Rectangle extends Shape {
    width: number;
    height: number;

    constructor(width: number, height: number) {
        super();
        this.width = width;
        this.height = height;
    }

    area(): number {
        return this.width * this.height;
    }
}
```

</td>
<td>

```csharp
abstract class Shape
{
    public abstract double Area();

    public string Describe()
    {
        return "A shape with area " + Area();
    }
}

class Rectangle : Shape
{
    public double Width { get; set; }
    public double Height { get; set; }

    public Rectangle(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public override double Area()
    {
        return Width * Height;
    }
}
```

</td>
</tr>
</table>

---

## Interfaces

### Basic Interface

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
interface Point {
    x: number;
    y: number;
}

const origin: Point = { x: 0, y: 0 };

function distance(p1: Point, p2: Point): number {
    const dx: number = p2.x - p1.x;
    const dy: number = p2.y - p1.y;
    return Math.sqrt(dx * dx + dy * dy);
}
```

</td>
<td>

```csharp
// Interfaces are compile-time only
// Object literals become dictionaries
interface IPoint
{
    double X { get; set; }
    double Y { get; set; }
}

var origin = new Dictionary<string, object>
{
    ["x"] = 0.0,
    ["y"] = 0.0
};
```

</td>
</tr>
</table>

### Class Implementing Interface

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
interface Named {
    name: string;
    getName(): string;
}

class User implements Named {
    name: string;

    constructor(name: string) {
        this.name = name;
    }

    getName(): string {
        return this.name;
    }
}
```

</td>
<td>

```csharp
interface INamed
{
    string Name { get; set; }
    string GetName();
}

class User : INamed
{
    public string Name { get; set; }

    public User(string name)
    {
        Name = name;
    }

    public string GetName()
    {
        return Name;
    }
}
```

</td>
</tr>
</table>

---

## Enums

### Numeric Enums

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
enum Direction {
    Up,      // 0
    Down,    // 1
    Left,    // 2
    Right    // 3
}

const dir: Direction = Direction.Up;
console.log(dir);              // 0
console.log(Direction[0]);     // "Up" (reverse mapping)
```

</td>
<td>

```csharp
enum Direction
{
    Up = 0,
    Down = 1,
    Left = 2,
    Right = 3
}

var dir = Direction.Up;
Console.WriteLine((int)dir);   // 0
// SharpTS supports reverse mapping:
// Direction[0] returns "Up"
```

</td>
</tr>
</table>

### Enums with Custom Values

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
enum HttpStatus {
    OK = 200,
    NotFound = 404,
    ServerError = 500
}

const status: HttpStatus = HttpStatus.OK;
```

</td>
<td>

```csharp
enum HttpStatus
{
    OK = 200,
    NotFound = 404,
    ServerError = 500
}

var status = HttpStatus.OK;
```

</td>
</tr>
</table>

### String Enums

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
enum Color {
    Red = "RED",
    Green = "GREEN",
    Blue = "BLUE"
}

const c: Color = Color.Red;  // "RED"
```

</td>
<td>

```csharp
// String enums become static constants
static class Color
{
    public const string Red = "RED";
    public const string Green = "GREEN";
    public const string Blue = "BLUE";
}

var c = Color.Red;  // "RED"
```

</td>
</tr>
</table>

---

## Arrays and Collections

### Array Basics

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const numbers: number[] = [1, 2, 3, 4, 5];
const first: number = numbers[0];
const len: number = numbers.length;

numbers.push(6);
const last: number = numbers.pop();
```

</td>
<td>

```csharp
var numbers = new List<object>
    { 1.0, 2.0, 3.0, 4.0, 5.0 };
var first = (double)numbers[0];
var len = numbers.Count;

numbers.Add(6.0);
var last = numbers[^1];
numbers.RemoveAt(numbers.Count - 1);
```

</td>
</tr>
</table>

### Array Methods

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const nums: number[] = [1, 2, 3, 4, 5];

// map
const doubled = nums.map(
    (n: number): number => n * 2
);
// [2, 4, 6, 8, 10]

// filter
const evens = nums.filter(
    (n: number): boolean => n % 2 === 0
);
// [2, 4]

// find
const found = nums.find(
    (n: number): boolean => n > 3
);
// 4

// reduce
const sum = nums.reduce(
    (acc: number, n: number): number => acc + n,
    0
);
// 15
```

</td>
<td>

```csharp
var nums = new List<double> { 1, 2, 3, 4, 5 };

// map
var doubled = nums
    .Select(n => n * 2)
    .ToList();
// [2, 4, 6, 8, 10]

// filter
var evens = nums
    .Where(n => n % 2 == 0)
    .ToList();
// [2, 4]

// find
var found = nums
    .First(n => n > 3);
// 4

// reduce
var sum = nums
    .Aggregate(0.0, (acc, n) => acc + n);
// 15
```

</td>
</tr>
</table>

### Map

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const map = new Map<string, number>();
map.set("one", 1);
map.set("two", 2);

map.get("one");      // 1
map.has("two");      // true
map.delete("one");
map.size;            // 1
```

</td>
<td>

```csharp
var map = new Dictionary<string, double>();
map["one"] = 1;
map["two"] = 2;

map["one"];              // 1
map.ContainsKey("two");  // true
map.Remove("one");
map.Count;               // 1
```

</td>
</tr>
</table>

### Set

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const set = new Set<number>();
set.add(1);
set.add(2);
set.add(1);  // duplicate ignored

set.has(1);     // true
set.size;       // 2
set.delete(1);
```

</td>
<td>

```csharp
var set = new HashSet<double>();
set.Add(1);
set.Add(2);
set.Add(1);  // duplicate ignored

set.Contains(1);  // true
set.Count;        // 2
set.Remove(1);
```

</td>
</tr>
</table>

### Object Literals

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const person = {
    name: "Alice",
    age: 30,
    greet(): string {
        return "Hello, " + this.name;
    }
};

person.name;     // "Alice"
person.greet();  // "Hello, Alice"
```

</td>
<td>

```csharp
// Object literals become dictionaries
var person = new Dictionary<string, object>
{
    ["name"] = "Alice",
    ["age"] = 30.0,
    ["greet"] = new Func<string>(
        () => "Hello, " + person["name"])
};

person["name"];                      // "Alice"
((Func<string>)person["greet"])();   // "Hello, Alice"
```

</td>
</tr>
</table>

---

## Control Flow

### Conditionals

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function classify(n: number): string {
    if (n < 0) {
        return "negative";
    } else if (n === 0) {
        return "zero";
    } else {
        return "positive";
    }
}
```

</td>
<td>

```csharp
string Classify(double n)
{
    if (n < 0)
    {
        return "negative";
    }
    else if (n == 0)
    {
        return "zero";
    }
    else
    {
        return "positive";
    }
}
```

</td>
</tr>
</table>

### Switch Statement

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function getDayName(day: number): string {
    switch (day) {
        case 0:
            return "Sunday";
        case 1:
            return "Monday";
        case 2:
            return "Tuesday";
        default:
            return "Unknown";
    }
}
```

</td>
<td>

```csharp
string GetDayName(double day)
{
    switch ((int)day)
    {
        case 0:
            return "Sunday";
        case 1:
            return "Monday";
        case 2:
            return "Tuesday";
        default:
            return "Unknown";
    }
}
```

</td>
</tr>
</table>

### Loops

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
// while loop
let i: number = 0;
while (i < 5) {
    console.log(i);
    i = i + 1;
}

// for...of loop
const items: string[] = ["a", "b", "c"];
for (const item of items) {
    console.log(item);
}

// for loop
for (let j: number = 0; j < 3; j = j + 1) {
    console.log(j);
}
```

</td>
<td>

```csharp
// while loop
int i = 0;
while (i < 5)
{
    Console.WriteLine(i);
    i = i + 1;
}

// foreach loop
var items = new[] { "a", "b", "c" };
foreach (var item in items)
{
    Console.WriteLine(item);
}

// for loop
for (int j = 0; j < 3; j++)
{
    Console.WriteLine(j);
}
```

</td>
</tr>
</table>

### Try/Catch/Finally

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function divide(a: number, b: number): number {
    try {
        if (b === 0) {
            throw new Error("Division by zero");
        }
        return a / b;
    } catch (e) {
        console.log("Error: " + e.message);
        return 0;
    } finally {
        console.log("Operation complete");
    }
}
```

</td>
<td>

```csharp
double Divide(double a, double b)
{
    try
    {
        if (b == 0)
        {
            throw new Exception("Division by zero");
        }
        return a / b;
    }
    catch (Exception e)
    {
        Console.WriteLine("Error: " + e.Message);
        return 0;
    }
    finally
    {
        Console.WriteLine("Operation complete");
    }
}
```

</td>
</tr>
</table>

---

## Advanced Features

### Async/Await

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
async function fetchData(
    url: string
): Promise<string> {
    const response = await getData(url);
    return response;
}

async function main(): Promise<void> {
    const data = await fetchData(
        "https://api.example.com"
    );
    console.log(data);
}
```

</td>
<td>

```csharp
async Task<string> FetchData(
    string url)
{
    var response = await GetData(url);
    return response;
}

async Task Main()
{
    var data = await FetchData(
        "https://api.example.com"
    );
    Console.WriteLine(data);
}
```

</td>
</tr>
</table>

### Generators

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function* counter(): Generator<number> {
    let i: number = 0;
    while (true) {
        yield i;
        i = i + 1;
    }
}

const gen = counter();
gen.next().value;  // 0
gen.next().value;  // 1
gen.next().value;  // 2
```

</td>
<td>

```csharp
IEnumerable<double> Counter()
{
    double i = 0;
    while (true)
    {
        yield return i;
        i = i + 1;
    }
}

var gen = Counter().GetEnumerator();
gen.MoveNext(); // gen.Current = 0
gen.MoveNext(); // gen.Current = 1
gen.MoveNext(); // gen.Current = 2
```

</td>
</tr>
</table>

### Template Literals

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const name: string = "World";
const greeting = `Hello, ${name}!`;

const a: number = 5;
const b: number = 3;
const result = `${a} + ${b} = ${a + b}`;
// "5 + 3 = 8"
```

</td>
<td>

```csharp
var name = "World";
var greeting = $"Hello, {name}!";

var a = 5.0;
var b = 3.0;
var result = $"{a} + {b} = {a + b}";
// "5 + 3 = 8"
```

</td>
</tr>
</table>

### Destructuring

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
// Array destructuring
const [first, second, ...rest] = [1, 2, 3, 4, 5];
// first = 1, second = 2, rest = [3, 4, 5]

// Object destructuring
const { name, age } = { name: "Alice", age: 30 };

// With renaming
const { name: userName } = { name: "Bob" };
// userName = "Bob"
```

</td>
<td>

```csharp
// Array destructuring (manual)
var arr = new[] { 1.0, 2.0, 3.0, 4.0, 5.0 };
var first = arr[0];
var second = arr[1];
var rest = arr.Skip(2).ToArray();

// Object destructuring (manual)
var obj = new Dictionary<string, object>
    { ["name"] = "Alice", ["age"] = 30.0 };
var name = (string)obj["name"];
var age = (double)obj["age"];
```

</td>
</tr>
</table>

### Spread Operator

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
// Array spread
const arr1: number[] = [1, 2, 3];
const arr2: number[] = [...arr1, 4, 5];
// [1, 2, 3, 4, 5]

// Object spread
const obj1 = { a: 1, b: 2 };
const obj2 = { ...obj1, c: 3 };
// { a: 1, b: 2, c: 3 }
```

</td>
<td>

```csharp
// Array spread
var arr1 = new List<double> { 1, 2, 3 };
var arr2 = arr1
    .Concat(new[] { 4.0, 5.0 })
    .ToList();
// [1, 2, 3, 4, 5]

// Object spread
var obj1 = new Dictionary<string, object>
    { ["a"] = 1.0, ["b"] = 2.0 };
var obj2 = new Dictionary<string, object>(obj1)
    { ["c"] = 3.0 };
// { a: 1, b: 2, c: 3 }
```

</td>
</tr>
</table>

### Optional Chaining

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const user = {
    profile: { name: "Alice" }
};
const name = user?.profile?.name;  // "Alice"

const missing = null;
const value = missing?.property;   // undefined
```

</td>
<td>

```csharp
var user = new {
    profile = new { name = "Alice" }
};
var name = user?.profile?.name;  // "Alice"

object missing = null;
var value = (missing as dynamic)?.property;  // null
```

</td>
</tr>
</table>

### Nullish Coalescing

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
const value = null ?? "default";  // "default"
const zero = 0 ?? 42;             // 0 (not null)
```

</td>
<td>

```csharp
var value = null ?? "default";  // "default"
var zero = 0.0 ?? 42.0;         // 0 (not null)
```

</td>
</tr>
</table>

### Generics

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
function identity<T>(value: T): T {
    return value;
}

identity<number>(42);      // 42
identity<string>("hello"); // "hello"

class Box<T> {
    value: T;

    constructor(value: T) {
        this.value = value;
    }

    getValue(): T {
        return this.value;
    }
}

const numBox = new Box<number>(42);
numBox.getValue();  // 42
```

</td>
<td>

```csharp
T Identity<T>(T value)
{
    return value;
}

Identity<double>(42);       // 42
Identity<string>("hello");  // "hello"

class Box<T>
{
    public T Value { get; set; }

    public Box(T value)
    {
        Value = value;
    }

    public T GetValue()
    {
        return Value;
    }
}

var numBox = new Box<double>(42);
numBox.GetValue();  // 42
```

</td>
</tr>
</table>

---

## Modules

### Named Exports/Imports

<table>
<tr>
<th>TypeScript (math.ts)</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
export const PI: number = 3.14159;

export function add(
    a: number,
    b: number
): number {
    return a + b;
}

export function multiply(
    a: number,
    b: number
): number {
    return a * b;
}
```

</td>
<td>

```csharp
// Exports become public static members
public static class MathModule
{
    public const double PI = 3.14159;

    public static double Add(
        double a,
        double b)
    {
        return a + b;
    }

    public static double Multiply(
        double a,
        double b)
    {
        return a * b;
    }
}
```

</td>
</tr>
</table>

<table>
<tr>
<th>TypeScript (main.ts)</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
import { PI, add, multiply } from './math';

console.log(PI);           // 3.14159
console.log(add(2, 3));    // 5
```

</td>
<td>

```csharp
using static MathModule;

Console.WriteLine(PI);      // 3.14159
Console.WriteLine(Add(2, 3)); // 5
```

</td>
</tr>
</table>

### Default Exports

<table>
<tr>
<th>TypeScript (greeter.ts)</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
export default function greet(
    name: string
): string {
    return "Hello, " + name;
}
```

</td>
<td>

```csharp
public static class GreeterModule
{
    public static string Greet(string name)
    {
        return "Hello, " + name;
    }
}
```

</td>
</tr>
</table>

<table>
<tr>
<th>TypeScript (main.ts)</th>
<th>C# Equivalent</th>
</tr>
<tr>
<td>

```typescript
import greet from './greeter';

greet("World");  // "Hello, World"
```

</td>
<td>

```csharp
using static GreeterModule;

Greet("World");  // "Hello, World"
```

</td>
</tr>
</table>

---

## Decorators

> **Note:** Enable decorators with `--experimentalDecorators` (Stage 2) or `--decorators` (Stage 3).

### Class Decorator

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent (Conceptual)</th>
</tr>
<tr>
<td>

```typescript
function logged(target: any): any {
    console.log("Class created: " + target.name);
    return target;
}

@logged
class MyClass {
    constructor() {
        console.log("Instance created");
    }
}
```

</td>
<td>

```csharp
// Decorators are applied at runtime
// Similar to attributes + reflection
[Logged]
class MyClass
{
    public MyClass()
    {
        Console.WriteLine("Instance created");
    }
}

// Decorator logic runs when class is defined
```

</td>
</tr>
</table>

### Method Decorator

<table>
<tr>
<th>TypeScript</th>
<th>C# Equivalent (Conceptual)</th>
</tr>
<tr>
<td>

```typescript
function log(
    target: any,
    key: string,
    descriptor: PropertyDescriptor
): PropertyDescriptor {
    const original = descriptor.value;
    descriptor.value = function(...args: any[]) {
        console.log("Calling " + key);
        return original.apply(this, args);
    };
    return descriptor;
}

class Calculator {
    @log
    add(a: number, b: number): number {
        return a + b;
    }
}
```

</td>
<td>

```csharp
// Method decorators wrap the original method
class Calculator
{
    public double Add(double a, double b)
    {
        // Decorator wraps this call
        Console.WriteLine("Calling Add");
        return a + b;
    }
}
```

</td>
</tr>
</table>

---

## Type Mapping Reference

| TypeScript | .NET Type | Notes |
|------------|-----------|-------|
| `number` | `double` | All numbers are 64-bit floats |
| `string` | `string` | Direct mapping |
| `boolean` | `bool` | Direct mapping |
| `null` | `null` | Represented as null object |
| `undefined` | `null` | Treated as null at runtime |
| `any` | `object` | Dynamic typing |
| `unknown` | `object` | Requires type checking |
| `void` | `void` | No return value |
| `never` | `void` | Function never returns |
| `bigint` | `BigInteger` | Arbitrary precision |
| `symbol` | Custom | Runtime symbol type |
| `Array<T>` | `object[]` | Runtime array wrapper |
| `T[]` | `object[]` | Same as Array&lt;T&gt; |
| `Promise<T>` | `Task<T>` | Async operations |
| `Map<K,V>` | `Dictionary<K,V>` | Key-value collection |
| `Set<T>` | `HashSet<T>` | Unique values |
| `Date` | `DateTime` | Date/time operations |
| `RegExp` | `Regex` | Regular expressions |
| Object literal | `Dictionary<string,object>` | Dynamic properties |
| Class | Generated class | IL class definition |
| Interface | Compile-time only | Structural typing |
| Enum | Generated enum | With reverse mapping |
| Union types | `object` | Runtime type checking |
| Function | `TSFunction` | Callable wrapper |

---

## What's Not Supported

SharpTS focuses on core TypeScript features. The following are not currently supported:

- **Namespaces** (use modules instead)
- **Declaration merging**
- **Ambient declarations** (`.d.ts` files)
- **JSX/TSX**
- **Decorators on parameters** (class and method decorators only)
- **`eval()`** and dynamic code execution
- **Prototype manipulation**
- **`with` statement**

---

## Running the Examples

**Interpreted mode (development):**
```bash
dotnet run -- example.ts
```

**Compiled mode (production):**
```bash
dotnet run -- --compile example.ts
dotnet example.dll
```

See [Execution Modes](./execution-modes.md) for more details on when to use each mode.
