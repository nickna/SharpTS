function* values(){try{yield 2;yield 3;}finally{console.log("closed");}}const helper=Iterator.from(values()).take(1);console.log(helper.toArray().join(","));console.log(helper.next().done);
