enum E{A=2,B=3}function* names(){const alias:any=E;yield alias[2];yield alias===E;yield E[3];}const g=names();console.log(g.next().value,g.next().value,g.next().value);
