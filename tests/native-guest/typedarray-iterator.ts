let i=0;
            const source:any={get length(){throw 'length must not be read';},[Symbol.iterator](){
                return {next(){console.log('next',i);if(i++===2)return {done:true};
                    return {done:false,value:{valueOf(){console.log('convert');return 7;}}};}};
            }};
            const value=new Uint8Array(source);console.log(value.length,value[0],value[1]);
