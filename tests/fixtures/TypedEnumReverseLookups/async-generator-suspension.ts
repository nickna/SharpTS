enum E{A=2,B=3}async function* names(key:number){await Promise.resolve(1);yield E[key];key=2;yield E[key];}async function run(){for await(const value of names(3))console.log(value);}run();
