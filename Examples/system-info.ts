// System Info - Display comprehensive system information
// Usage: sharpts examples/system-info.ts
//
// Demonstrates: os (platform, arch, hostname, cpus, totalmem, freemem, homedir, tmpdir, userInfo, EOL)
//               process (pid, version, cwd, env, argv, uptime, memoryUsage)

import os from 'os';
import process from 'process';

function formatBytes(bytes: number): string {
    const gb = bytes / (1024 * 1024 * 1024);
    return gb.toFixed(2) + ' GB';
}

function formatUptime(seconds: number): string {
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
    return hours + 'h ' + minutes + 'm ' + secs + 's';
}

function main(): void {
    console.log('System Information Report');
    console.log('=========================');
    console.log('');

    // Operating System
    console.log('Operating System');
    console.log('----------------');
    console.log('Platform:     ' + os.platform());
    console.log('Type:         ' + os.type());
    console.log('Release:      ' + os.release());
    console.log('Architecture: ' + os.arch());
    console.log('Hostname:     ' + os.hostname());
    console.log('');

    // Memory
    console.log('Memory');
    console.log('------');
    const totalMem = os.totalmem();
    const freeMem = os.freemem();
    const usedMem = totalMem - freeMem;
    const usedPercent = ((usedMem / totalMem) * 100).toFixed(1);
    console.log('Total:  ' + formatBytes(totalMem));
    console.log('Free:   ' + formatBytes(freeMem));
    console.log('Used:   ' + formatBytes(usedMem) + ' (' + usedPercent + '%)');
    console.log('');

    // CPU
    console.log('CPU');
    console.log('---');
    const cpus = os.cpus();
    console.log('Cores: ' + cpus.length);
    if (cpus.length > 0) {
        console.log('Model: ' + cpus[0].model);
        console.log('Speed: ' + cpus[0].speed + ' MHz');
    }
    console.log('');

    // User & Paths
    console.log('User & Paths');
    console.log('------------');
    const user = os.userInfo();
    console.log('Username: ' + user.username);
    console.log('Home:     ' + os.homedir());
    console.log('Temp:     ' + os.tmpdir());
    console.log('CWD:      ' + process.cwd());
    console.log('');

    // Process
    console.log('Process');
    console.log('-------');
    console.log('PID:     ' + process.pid);
    console.log('Version: ' + process.version);
    console.log('Uptime:  ' + formatUptime(process.uptime()));
    console.log('');

    // Process Memory
    const mem = process.memoryUsage();
    console.log('Process Memory');
    console.log('--------------');
    console.log('Heap Total: ' + (mem.heapTotal / 1024 / 1024).toFixed(2) + ' MB');
    console.log('Heap Used:  ' + (mem.heapUsed / 1024 / 1024).toFixed(2) + ' MB');
    console.log('');

    // Environment (selected vars)
    console.log('Environment (selected)');
    console.log('----------------------');
    const envVars = ['PATH', 'HOME', 'USER', 'USERPROFILE', 'TEMP', 'TMP'];
    for (const key of envVars) {
        const value = process.env[key];
        if (value != null) {
            const display = value.length > 60 ? value.substring(0, 57) + '...' : value;
            console.log(key + ': ' + display);
        }
    }
}

main();
