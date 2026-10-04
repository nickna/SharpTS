namespace Models {export class Box<T> {value:T;constructor(value:T){this.value=value;}read():T{return this.value;}}}console.log(new Models.Box<number>(3).read(),new Models.Box<string>("ok").read());
