"use strict";namespace Values {export const value=3;}const values:any=Values;console.log(delete values["value"],values.value===undefined,delete values.missing);
