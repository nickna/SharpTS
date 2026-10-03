function* values(){try{yield 2;yield 3;}finally{console.log("closed");}}const [a]=values();console.log(a);
