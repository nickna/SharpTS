console.log(Iterator.from([1,2,3,4]).drop(1).take(2).toArray().join(","));try{Iterator.from([1]).take(-1);console.log("accepted");}catch(e){console.log(e.name);}
