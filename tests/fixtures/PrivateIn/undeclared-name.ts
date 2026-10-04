// Negative syntax control: the lexical private name must be declared.
class Box { static has(value: any) { return #missing in value; } }
