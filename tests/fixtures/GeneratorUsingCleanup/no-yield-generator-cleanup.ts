function* values(){using r={[Symbol.dispose](){console.log("dispose");}};return 7;}const g=values();console.log(g.next().value,g.next().done);
