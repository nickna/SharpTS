function* run(){const holder:any={evaluate:eval};yield holder.evaluate(String("1+2"));}console.log(run().next().value);
