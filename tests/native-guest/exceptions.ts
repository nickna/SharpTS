try { throw { message: 'guest error', code: 42 }; }
catch (error) { console.log(error.message, error.code); }
