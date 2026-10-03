enum Labels{A="a",B="b"}enum Numeric{A=2,B=2,C=3}const text:any=Labels;const numeric:any=Numeric;console.log(text===Labels,text.A,text.a===undefined,numeric===Numeric,numeric[2],numeric.C);
