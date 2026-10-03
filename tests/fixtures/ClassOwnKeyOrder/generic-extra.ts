class Box<T>{value:T;constructor(value:T){this.value=value;}}const b:any=new Box<string>("text");b.extra=9;b.value="next";console.log(Object.keys(b).join(","),Object.values(b).join(","));
