const from: any = Array.from;
class ConstructorOnly {}
console.log(from([], ConstructorOnly).length);
try { from([1], ConstructorOnly); console.log('accepted'); } catch (error) { console.log(error instanceof TypeError); }
