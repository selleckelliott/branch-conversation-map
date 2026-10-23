import {spawn} from 'node:child_process';
import {existsSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
import readline from 'node:readline';

export const root=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
export const executable=path.join(root,'desktop','Branch.Desktop','bin','Release','net8.0-windows','Branch.Desktop.exe');
export const toolsList=[
 {name:'branch_open_desktop_map',description:'Open the native Windows Branch conversation map over Codex desktop. Use when the user asks to show their conversation map. This opens a native application; no website or local web server is used. Chat selection, forks, and reviewed merges happen in the overlay.',inputSchema:{type:'object',properties:{},additionalProperties:false},annotations:{readOnlyHint:false,destructiveHint:false,openWorldHint:false}},
 {name:'branch_desktop_status',description:'Check whether the native Branch desktop overlay is built and available on this computer. Does not read chats.',inputSchema:{type:'object',properties:{},additionalProperties:false},annotations:{readOnlyHint:true,openWorldHint:false}}
];
export async function handleDesktopRPC(request,launch=defaultLaunch){
 const result=value=>({jsonrpc:'2.0',id:request.id,result:value});
 if(request.id===undefined)return null;
 if(request.method==='initialize')return result({protocolVersion:'2024-11-05',capabilities:{tools:{}},serverInfo:{name:'branch-desktop',version:'0.3.0'}});
 if(request.method==='ping')return result({});
 if(request.method==='tools/list')return result({tools:toolsList});
 if(request.method==='tools/call'){
  try{
   const name=request.params?.name;
   if(name==='branch_desktop_status')return result({content:[{type:'text',text:JSON.stringify({platform:process.platform,built:existsSync(executable),executable})}]});
   if(name!=='branch_open_desktop_map')throw Error('Unknown tool.');
   if(process.platform!=='win32')throw Error('This first native overlay supports Windows.');
   if(!existsSync(executable))throw Error('Build the native overlay first: dotnet build desktop/Branch.Desktop/Branch.Desktop.csproj -c Release --configfile desktop/Branch.Desktop/NuGet.Config');
   await launch();return result({content:[{type:'text',text:'Opened the native Branch overlay. Ctrl+Shift+Space toggles it. Choose the Codex window and enable Follow Codex to attach it. The existing Codex sidebar and composer stay available.'}]});
  }catch(error){return result({isError:true,content:[{type:'text',text:error.message}]});}
 }
 return {jsonrpc:'2.0',id:request.id,error:{code:-32601,message:'Method not found'}};
}
function defaultLaunch(){return new Promise((resolve,reject)=>{const child=spawn(executable,[],{cwd:root,detached:true,stdio:'ignore',windowsHide:false});child.once('error',reject);child.once('spawn',()=>{child.unref();resolve();});});}
if(process.argv[1]&&fileURLToPath(import.meta.url)===process.argv[1]){
 for await(const line of readline.createInterface({input:process.stdin})){
  let reply;try{reply=await handleDesktopRPC(JSON.parse(line));}catch{reply={jsonrpc:'2.0',id:null,error:{code:-32700,message:'Parse error'}};}
  if(reply)process.stdout.write(JSON.stringify(reply)+'\n');
 }
}
