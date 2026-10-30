import * as net from 'node:net';
let client: any = null;
let accepted: any = null;
let finished = false;
let deadline: any = null;
function fail(): void {
    if (finished) return;
    finished = true;
    console.error('loopback-failed');
    process.exitCode = 1;
    clearTimeout(deadline);
    if (client !== null) client.destroy();
    if (accepted !== null) accepted.destroy();
    if (server.listening) server.close();
}
const server: any = net.createServer((socket: any) => {
    accepted = socket;
    let request = '';
    socket.on('error', fail);
    socket.on('data', (chunk: any) => {
        request += chunk.toString();
        if (request === 'ping') socket.end('pong');
    });
});
server.on('error', fail);
deadline = setTimeout(fail, 5000);
server.listen(0, '127.0.0.1', () => {
    client = net.createConnection({ port: server.address().port, host: '127.0.0.1' });
    let response = '';
    client.on('error', fail);
    client.on('connect', () => { client.write('ping'); });
    client.on('data', (chunk: any) => { response += chunk.toString(); });
    client.on('end', () => {
        if (response !== 'pong') { fail(); return; }
        client.destroy();
        server.close(() => {
            if (finished) return;
            finished = true;
            clearTimeout(deadline);
            console.log('loopback=pong');
            console.log('closed');
        });
    });
});
