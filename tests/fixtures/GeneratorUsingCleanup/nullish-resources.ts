function* values(){using a:any=null;using b:any=undefined;yield 1;return 2;}const g:any=values();console.log(g.next().value);console.log(g.next().value,g.next().done);
