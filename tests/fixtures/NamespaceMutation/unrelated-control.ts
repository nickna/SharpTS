namespace Left {export let value=1;}namespace Right {export let value=2;}const left:any=Left;left.value=7;console.log(left.value,Left.value,Right.value);
