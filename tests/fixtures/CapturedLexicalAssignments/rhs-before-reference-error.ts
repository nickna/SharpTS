function outer(){let log="";function rhs(){log+="rhs";return 1;}const set=()=>{x=rhs();};try{set();}catch(e){console.log(e.name,log);}let x=2;console.log(x);}outer();
