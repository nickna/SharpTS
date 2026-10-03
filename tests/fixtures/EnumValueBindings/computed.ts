enum Flags {One=1,Two=1<<1,All=One|Two}let key:number=Flags.All;console.log(Flags[key]);
