function Inner(this:any){this.y=2;}const receiver:any={y:90};const bound:any=Inner.bind(receiver);const b:any=new bound();console.log(b.y,receiver.y,b===receiver);
