// SharpTS C# Interop Example
// Demonstrates consuming SharpTS-compiled TypeScript from C# using reflection

using System.Reflection;

Console.WriteLine("=== SharpTS C# Interop Example ===");
Console.WriteLine("Loading compiled TypeScript via reflection");
Console.WriteLine();

// Load the compiled TypeScript assembly
var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Library.dll");
var assembly = Assembly.LoadFrom(assemblyPath);
Console.WriteLine($"Loaded assembly: {assembly.FullName}");
Console.WriteLine();

// ============================================
// 1. Basic Class Instantiation and Methods
// ============================================
Console.WriteLine("--- 1. Class Instantiation and Methods ---");

var personType = assembly.GetType("Person")!;
var person = Activator.CreateInstance(personType, "Alice", 30.0)!;

// Property access via .NET PropertyInfo (PascalCase names)
var nameProp = personType.GetProperty("Name")!;
var ageProp = personType.GetProperty("Age")!;
Console.WriteLine($"Created Person: {nameProp.GetValue(person)}, age {ageProp.GetValue(person)}");

// Call method
var greet = personType.GetMethod("greet")!;
string greeting = (string)greet.Invoke(person, null)!;
Console.WriteLine($"Greeting: {greeting}");

// Modify state via method
var haveBirthday = personType.GetMethod("haveBirthday")!;
haveBirthday.Invoke(person, null);
Console.WriteLine($"After birthday: age is now {ageProp.GetValue(person)}");
Console.WriteLine();

// ============================================
// 2. Property Access (Get and Set)
// ============================================
Console.WriteLine("--- 2. Property Access ---");

var person2 = Activator.CreateInstance(personType, "Bob", 25.0)!;
Console.WriteLine($"Original name: {nameProp.GetValue(person2)}");

// Set property via PropertyInfo
nameProp.SetValue(person2, "Robert");
Console.WriteLine($"After rename: {nameProp.GetValue(person2)}");

ageProp.SetValue(person2, 26.0);
Console.WriteLine($"After age update: {ageProp.GetValue(person2)}");
Console.WriteLine();

// ============================================
// 3. Static Members
// ============================================
Console.WriteLine("--- 3. Static Members ---");

var calculatorType = assembly.GetType("Calculator")!;

// Static field access
var piField = calculatorType.GetField("PI", BindingFlags.Public | BindingFlags.Static)!;
double pi = (double)piField.GetValue(null)!;
Console.WriteLine($"Calculator.PI = {pi}");

// Static method calls
var addMethod = calculatorType.GetMethod("add", BindingFlags.Public | BindingFlags.Static)!;
object sum = addMethod.Invoke(null, [10.0, 20.0])!;
Console.WriteLine($"Calculator.add(10, 20) = {sum}");

var multiplyMethod = calculatorType.GetMethod("multiply", BindingFlags.Public | BindingFlags.Static)!;
object product = multiplyMethod.Invoke(null, [5.0, 6.0])!;
Console.WriteLine($"Calculator.multiply(5, 6) = {product}");
Console.WriteLine();

// ============================================
// 4. Instance Methods with State
// ============================================
Console.WriteLine("--- 4. Instance Methods with State ---");

var calc = Activator.CreateInstance(calculatorType)!;
var accumulatorProp = calculatorType.GetProperty("Accumulator")!;
Console.WriteLine($"Initial accumulator: {accumulatorProp.GetValue(calc)}");

var addToAccumulator = calculatorType.GetMethod("addToAccumulator")!;
object result1 = addToAccumulator.Invoke(calc, [5.0])!;
Console.WriteLine($"After adding 5: {result1}");

object result2 = addToAccumulator.Invoke(calc, [3.0])!;
Console.WriteLine($"After adding 3: {result2}");

var reset = calculatorType.GetMethod("reset")!;
reset.Invoke(calc, null);
Console.WriteLine($"After reset: {accumulatorProp.GetValue(calc)}");
Console.WriteLine();

// ============================================
// 5. Inheritance
// ============================================
Console.WriteLine("--- 5. Inheritance ---");

// Base class
var animalType = assembly.GetType("Animal")!;
var animal = Activator.CreateInstance(animalType, "Generic Animal")!;
var animalSpeak = animalType.GetMethod("speak")!;
string animalSound = (string)animalSpeak.Invoke(animal, null)!;
Console.WriteLine($"Animal speaks: {animalSound}");

// Derived class
var dogType = assembly.GetType("Dog")!;
var dog = Activator.CreateInstance(dogType, "Rex", "Golden Retriever")!;

// Overridden method
var dogSpeak = dogType.GetMethod("speak")!;
string dogSound = (string)dogSpeak.Invoke(dog, null)!;
Console.WriteLine($"Dog speaks: {dogSound}");

// Derived class method
var getInfo = dogType.GetMethod("getInfo")!;
string dogInfo = (string)getInfo.Invoke(dog, null)!;
Console.WriteLine($"Dog info: {dogInfo}");

// Property access on inherited and derived properties (PascalCase names)
var dogNameProp = dogType.GetProperty("Name")!;
var dogBreedProp = dogType.GetProperty("Breed")!;
Console.WriteLine($"Dog's name (inherited): {dogNameProp.GetValue(dog)}");
Console.WriteLine($"Dog's breed: {dogBreedProp.GetValue(dog)}");
Console.WriteLine();

// ============================================
// 6. List Types in Assembly
// ============================================
Console.WriteLine("--- 6. Types in Assembly ---");

foreach (var type in assembly.GetTypes())
{
    if (!type.Name.StartsWith("$") && !type.Name.StartsWith("<"))
    {
        Console.WriteLine($"  - {type.Name}");
    }
}
Console.WriteLine();

// ============================================
// 7. Top-level Functions
// ============================================
Console.WriteLine("--- 7. Top-level Functions ---");

// Top-level functions live on '$Program' class (requires reflection due to '$' in name)
var programType = assembly.GetType("$Program")!;
var formatMessage = programType.GetMethod("formatMessage", BindingFlags.Public | BindingFlags.Static)!;
object formatted = formatMessage.Invoke(null, ["INFO", "This is a test message"])!;
Console.WriteLine($"Formatted message: {formatted}");
Console.WriteLine();

// ============================================
// Summary
// ============================================
Console.WriteLine("=== All demonstrations completed successfully! ===");
Console.WriteLine();
Console.WriteLine("Key takeaways:");
Console.WriteLine("  - Assembly loaded via Assembly.LoadFrom()");
Console.WriteLine("  - Types accessed via assembly.GetType()");
Console.WriteLine("  - Instances created with Activator.CreateInstance()");
Console.WriteLine("  - Properties accessed via GetProperty() with PascalCase names");
Console.WriteLine("  - Methods invoked with MethodInfo.Invoke()");
Console.WriteLine("  - Static members via BindingFlags.Static");
Console.WriteLine("  - Inheritance works with method overriding");
Console.WriteLine("  - Top-level functions on '$Program' class");

return 0;
