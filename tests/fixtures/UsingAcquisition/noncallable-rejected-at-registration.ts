for(const value of [42,null,undefined]){const r:any={[Symbol.dispose]:value};try{{using x=r;console.log("unreachable");}}catch(e){console.log(e.name,e instanceof TypeError);}}
