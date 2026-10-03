enum Flags {One=1,Two=1<<1,All=One|Two}let key:number=Flags.All;try{console.log(Flags[key]);}catch(e){console.log("computed-miss");}console.log(Flags.One,Flags.Two,Flags.All);
