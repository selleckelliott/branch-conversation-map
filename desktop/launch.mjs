import {spawn} from 'node:child_process';
import {existsSync} from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const executable=path.join(root,'desktop','Branch.Desktop','bin','Release','net8.0-windows','Branch.Desktop.exe');
if(process.platform!=='win32'){console.error('The native Branch overlay currently supports Windows.');process.exitCode=1;}
else{
 let ready=true;
 if(!existsSync(executable)){
  const code=await new Promise((resolve,reject)=>{const child=spawn('dotnet',['build',path.join(root,'desktop','Branch.Desktop','Branch.Desktop.csproj'),'-c','Release','--configfile',path.join(root,'desktop','Branch.Desktop','NuGet.Config')],{cwd:root,stdio:'inherit',windowsHide:true});child.on('error',reject);child.on('exit',resolve);}).catch(error=>{console.error(error.message);return 1;});
  ready=code===0;
 }
 if(ready){const child=spawn(executable,[],{cwd:root,detached:true,stdio:'ignore',windowsHide:false});child.on('error',error=>{console.error(error.message);process.exitCode=1;});child.on('spawn',()=>{child.unref();console.log('Branch desktop overlay opened. Ctrl+Shift+Space toggles the map.');});}
 else process.exitCode=1;
}
