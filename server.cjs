const http = require('node:http');
const fs = require('node:fs/promises');
const path = require('node:path');
async function start({port=Number(process.argv[2])||5180,directory=path.join(__dirname,'.branch')}={}){
 const {WorkspaceStore}=await import('./lib/store.mjs');const store=await new WorkspaceStore(directory).init();
 const files={'/':'index.html','/index.html':'index.html','/style.css':'style.css','/app.js':'app.js','/lib/model.mjs':'lib/model.mjs','/lib/import-chatgpt.mjs':'lib/import-chatgpt.mjs'};
 const server=http.createServer(async(req,res)=>{
  const reply=(status,value)=>{res.writeHead(status,{'Content-Type':'application/json','Cache-Control':'no-store','X-Content-Type-Options':'nosniff'});res.end(JSON.stringify(value));};
  try{
   const address=`127.0.0.1:${server.address().port}`;
   if(![address,`localhost:${server.address().port}`].includes(req.headers.host))return reply(403,{error:'Invalid host.'});
   if(req.headers.origin && ![`http://${address}`,`http://localhost:${server.address().port}`].includes(req.headers.origin))return reply(403,{error:'Cross-origin requests are not permitted.'});
   const url=new URL(req.url,`http://${address}`);
   if(req.method==='GET'&&url.pathname==='/api/workspace')return reply(200,store.snapshot());
   if(req.method==='GET'&&url.pathname==='/api/health')return reply(200,{ok:true,version:2});
   if(req.method==='GET'&&url.pathname==='/api/mcp-config')return reply(200,{mcpServers:{branch:{command:process.execPath,args:[path.join(__dirname,'mcp-server.mjs')],env:{BRANCH_URL:`http://${address}`}}}});
   if(req.method==='POST'&&url.pathname==='/api/command'){
    if(req.headers['x-branch-client']!=='1'||!req.headers['content-type']?.startsWith('application/json'))return reply(403,{error:'Use the Branch client.'});
    let body='',size=0;const chunks=[];for await(const chunk of req){size+=chunk.length;if(size>20*1024*1024)return reply(413,{error:'Import exceeds 20 MB. Import a smaller group of conversations.'});chunks.push(chunk);}body=Buffer.concat(chunks).toString('utf8');
    let input;try{input=JSON.parse(body);}catch{return reply(400,{error:'Invalid JSON.'});}
    const result=await store.command(input.command,input.revision);return reply(200,result);
   }
   if(req.method!=='GET')return reply(405,{error:'Method not allowed.'});
   const name=files[url.pathname];if(!name)return reply(404,{error:'Not found.'});
   const content=await fs.readFile(path.join(__dirname,name));res.writeHead(200,{'Content-Type':name.endsWith('.css')?'text/css':/\.m?js$/.test(name)?'text/javascript':'text/html','Cache-Control':'no-cache','X-Content-Type-Options':'nosniff','Referrer-Policy':'no-referrer'});res.end(content);
  }catch(error){console.error(error.message);if(!res.headersSent)reply(error.status||400,{error:error.message});else res.end();}
 });
 try{await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(port,'127.0.0.1',resolve);});}catch(error){await store.close();throw error;}
 console.log(`Branch is running at http://127.0.0.1:${server.address().port}`);return {server,store};
}
module.exports={start};
if(require.main===module)start().catch(error=>{console.error(error.message);process.exitCode=1;});
