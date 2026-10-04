enum E{A=(-2),B=+3,C=-(-4),D=-(0.5)}console.log(E.A,E.B,E.C,E.D);for(const key of [-2,3,4,-0.5]){console.log(E[key]);}
