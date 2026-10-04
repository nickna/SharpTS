enum E{A=0,B=2}const alias:any=E;console.log(alias[NaN]===undefined,alias[Infinity]===undefined,alias["absent"]===undefined,alias["2"],alias.A);
