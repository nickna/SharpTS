// Negative syntax control: private names cannot be used outside their class.
class Box { #field = 1; }
console.log(#field in new Box());
