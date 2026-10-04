function Inner(this:any){this.y=2;}
async function run(){
    const receiver:any={y:90};
    const bound:any=Inner.bind(receiver);
    await Promise.resolve();
    const instance:any=new bound();
    console.log(instance.y,receiver.y,instance===receiver);
}
run();
