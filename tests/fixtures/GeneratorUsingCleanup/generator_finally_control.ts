function* values(){try{yield 1;return 2;}finally{console.log("dispose");}}const g=values();console.log(g.next().value);console.log(g.return(9).value);
