function* values(){using r={ [Symbol.dispose](){console.log("dispose");} };yield 1;return 2;}const g=values();console.log(g.next().value);console.log(g.next().value);
