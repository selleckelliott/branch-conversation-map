const uuid = () => globalThis.crypto.randomUUID();
const now = () => new Date().toISOString();
export const SCHEMA = 2;
export function emptyWorkspace() { return {version: SCHEMA, revision: 0, folders: [], projects: []}; }
export function makeProject(name, folderId = null) {
  name = required(name, 'Project name', 100);
  return {id: uuid(), name, folderId, createdAt: now(), nodes: [{id: uuid(), title: name, type: 'Project root', summary: 'Every conversation starts with an idea.', parents: [], inherited: [], messages: [], status: 'Project', archived: false, createdAt: now()}]};
}
export function required(value, label, max = 1000000) { if (typeof value !== 'string' || !value.trim() || value.length > max) throw new Error(`${label} is required (maximum ${max} characters).`); return value.trim(); }
export function projectById(state, id) { const p = state.projects.find(p => p.id === id); if (!p) throw new Error('Project not found.'); return p; }
export function nodeById(project, id) { const n = project.nodes.find(n => n.id === id); if (!n) throw new Error('Conversation not found.'); return n; }
export function contextFor(node) { return [...node.inherited, ...node.messages.map(m => ({...m, sourceNodeId: node.id}))]; }
function textField(v, label, max) { if(typeof v !== 'string' || v.length > max) throw new Error(`Invalid ${label}.`); }
export function validateWorkspace(s) {
  if (!s || s.version !== SCHEMA || !Number.isSafeInteger(s.revision) || s.revision < 0 || !Array.isArray(s.projects) || !Array.isArray(s.folders) || s.projects.length > 500) throw new Error('Unsupported workspace format.');
  const unique = (items) => { const seen = new Set(); for(const i of items) { required(i.id, 'ID', 200); if(seen.has(i.id)) throw new Error('Duplicate IDs.'); seen.add(i.id); } return seen; };
  const folders = unique(s.folders); unique(s.projects);
  for(const f of s.folders) required(f.name, 'Folder name', 100);
  for(const p of s.projects) {
    required(p.name, 'Project name', 100); if(p.folderId !== null && !folders.has(p.folderId)) throw new Error('Missing folder.');
    if(!Array.isArray(p.nodes) || !p.nodes.length || p.nodes.length > 20000) throw new Error('Invalid project nodes.');
    const ids = unique(p.nodes), index = new Map(p.nodes.map(n=>[n.id,n]));
    unique(p.nodes.flatMap(n=>n.messages||[]));
    if(p.nodes.filter(n=>n.type==='Project root').length !== 1) throw new Error('Each project needs one root.');
    for(const n of p.nodes) {
      required(n.title,'Conversation title',200); textField(n.summary,'description',2000); textField(n.status,'status',100);
      if(!['Project root','Conversation','Branch','Merged branch'].includes(n.type) || typeof n.archived !== 'boolean') throw new Error('Invalid conversation type or archive state.');
      if(!Array.isArray(n.parents) || new Set(n.parents).size!==n.parents.length || n.parents.some(id=>!ids.has(id)||id===n.id)) throw new Error('Invalid conversation ancestry.');
      if(n.type==='Project root' ? n.parents.length!==0 : n.parents.length===0) throw new Error('Invalid root relationship.');
      if(!Array.isArray(n.inherited)||!Array.isArray(n.messages)) throw new Error('Invalid messages.');
      unique(n.messages); unique(n.inherited);
      for(const m of [...n.inherited,...n.messages]) { required(m.id,'Message ID',200); textField(m.role,'message role',100); textField(m.text,'message text',1000000); }
      for(const m of n.inherited) { if(!ids.has(m.sourceNodeId)) throw new Error('Missing source conversation.');const source=index.get(m.sourceNodeId).messages.find(original=>original.id===m.id);if(!source||source.role!==m.role||source.text!==m.text)throw new Error('Inherited context does not match its source message.'); }
      if(n.forkPoint && (!n.parents.includes(n.forkPoint.nodeId)||!contextFor(index.get(n.forkPoint.nodeId)).some(m=>m.id===n.forkPoint.messageId))) throw new Error('Invalid fork point.');
      if(n.sourceSnapshot) { if(!Array.isArray(n.sourceSnapshot)) throw new Error('Invalid source references.'); for(const ref of n.sourceSnapshot) { if(!n.parents.includes(ref.nodeId)||!Array.isArray(ref.messageIds)||ref.messageIds.some(id=>!contextFor(index.get(ref.nodeId)).some(m=>m.id===id))) throw new Error('Invalid source references.'); } }
    }
    const done=new Set(), visiting=new Set();
    function visit(n) { if(visiting.has(n.id)) throw new Error('Conversation graph contains a cycle.'); if(done.has(n.id))return; visiting.add(n.id); for(const id of n.parents)visit(index.get(id)); visiting.delete(n.id);done.add(n.id); }
    p.nodes.forEach(visit);
  }
  return s;
}
export function createBranch(project, {parentId, title, summary = '', messageId}) {
  const parent = nodeById(project,parentId); if(parent.archived)throw new Error('Restore this conversation before branching.');
  let inherited = parent.type==='Project root'?[]:contextFor(parent);
  if(messageId) { const at=inherited.findIndex(m=>m.id===messageId); if(at<0)throw new Error('Branch origin message not found.'); inherited=inherited.slice(0,at+1); }
  const n={id:uuid(),title:required(title,'Branch title',200),summary:summary.slice(0,2000),type:parent.type==='Project root'?'Conversation':'Branch',parents:[parent.id],inherited:structuredClone(inherited),messages:[],status:'Exploring',archived:false,createdAt:now()};
  if(inherited.length)n.forkPoint={nodeId:parent.id,messageId:inherited.at(-1).id};
  project.nodes.push(n); return n;
}
export function mergeBranches(project,{sourceIds,title,brief='',messageIds}) {
  if(!Array.isArray(sourceIds)||new Set(sourceIds).size<2||new Set(sourceIds).size!==sourceIds.length)throw new Error('Select at least two different conversations.');
  const sources=sourceIds.map(id=>nodeById(project,id));if(sources.some(n=>n.type==='Project root'||n.archived))throw new Error('Select active conversations to merge.');
  const contexts=sources.map(contextFor); const available=new Set(contexts.flat().map(m=>m.id));
  const selected=messageIds===undefined?available:new Set(messageIds);
  if(!selected.size||[...selected].some(id=>!available.has(id)))throw new Error('Choose valid source messages for the merge.');
  const seen=new Set(), inherited=[];for(const messages of contexts)for(const m of messages)if(selected.has(m.id)&&!seen.has(m.id)){seen.add(m.id);inherited.push(structuredClone(m));}
  const refs=sources.map((n,i)=>({nodeId:n.id,messageIds:contexts[i].filter(m=>selected.has(m.id)).map(m=>m.id),messageCount:contexts[i].length}));
  if(refs.some(r=>!r.messageIds.length))throw new Error('Include at least one message from each source.');
  const n={id:uuid(),title:required(title,'Merged branch title',200),summary:`Context from ${sources.map(n=>n.title).join(' + ')}`.slice(0,2000),type:'Merged branch',parents:[...sourceIds],inherited,messages:[],sourceSnapshot:refs,status:'Merged',archived:false,createdAt:now()};
  if(brief.trim())n.messages.push({id:uuid(),role:'Merge brief',text:required(brief,'Merge brief'),createdAt:now()});
  project.nodes.push(n);return n;
}
export function compareBranches(project,ids){
  if(ids.length!==2)throw new Error('Choose two conversations.');
  const [a,b]=ids.map(id=>contextFor(nodeById(project,id))),aIds=new Set(a.map(m=>m.id)),bIds=new Set(b.map(m=>m.id));
  return {shared:a.filter(m=>bIds.has(m.id)),left:a.filter(m=>!bIds.has(m.id)),right:b.filter(m=>!aIds.has(m.id))};
}
export function execute(state, command) {
  if(!command || typeof command.type!=='string')throw new Error('Missing command.');
  if(command.type==='project.create') { const p=makeProject(command.name,command.folderId||null);state.projects.push(p);return {projectId:p.id,nodeId:p.nodes[0].id}; }
  if(command.type==='folder.create') { const f={id:uuid(),name:required(command.name,'Folder name',100)};state.folders.push(f);return {folderId:f.id}; }
  if(command.type==='workspace.import') {const imported=normalizeImport(command.workspace);const existing=new Set(state.projects.map(p=>p.id)); if(imported.projects.some(p=>existing.has(p.id)))throw new Error('This workspace is already present. Import into a separate workspace or rename IDs first.'); const folderIds=new Set(state.folders.map(f=>f.id)); for(const f of imported.folders)if(!folderIds.has(f.id))state.folders.push(f);state.projects.push(...imported.projects);return {projectId:imported.projects[0]?.id};}
  const p=projectById(state,command.projectId);
  if(command.type==='project.update') { p.name=required(command.name,'Project name',100);p.folderId=command.folderId||null;p.nodes.find(n=>n.type==='Project root').title=p.name;return {}; }
  if(command.type==='branch.create'){const n=createBranch(p,command);return {nodeId:n.id};}
  if(command.type==='branch.merge'){const n=mergeBranches(p,command);return {nodeId:n.id};}
  const n=nodeById(p,command.nodeId);
  if(command.type==='message.add'){if(n.type==='Project root'||n.archived)throw new Error('Choose an active conversation.');const m={id:uuid(),role:['You','Assistant','Note'].includes(command.role)?command.role:'You',text:required(command.text,'Message'),createdAt:now()};n.messages.push(m);n.updatedAt=now();return {nodeId:n.id,messageId:m.id};}
  if(command.type==='node.update'){n.title=required(command.title,'Conversation title',200);n.summary=typeof command.summary==='string'?command.summary.slice(0,2000):n.summary;if(['Exploring','Decided','Open question','Merged'].includes(command.status))n.status=command.status;if(n.type==='Project root')p.name=n.title;return {};}
  if(command.type==='node.archive'){if(n.type==='Project root')throw new Error('The project root cannot be archived.');n.archived=!!command.archived;return {};}
  throw new Error('Unknown command.');
}
export function migrateLegacy(old){
  if(!old||typeof old.project!=='string'||!Array.isArray(old.nodes)||!old.nodes.length)throw new Error('Unrecognized workspace file.');
  const p=makeProject(old.project); const ids=new Map(old.nodes.map(n=>[n.id,uuid()]));
  p.nodes=old.nodes.map(n=>({id:ids.get(n.id),title:n.title,type:n.id==='root'?'Project root':n.type==='Merged branch'?'Merged branch':n.type==='Conversation'?'Conversation':'Branch',summary:n.summary||'',parents:(n.parents||[]).map(id=>ids.get(id)),messages:(n.messages||[]).map(m=>({id:uuid(),role:m.role,text:m.text,createdAt:now()})),inherited:[],status:n.status||'Exploring',archived:false,createdAt:now()}));
  return validateWorkspace({...emptyWorkspace(),projects:[p]});
}
export function normalizeImport(input){return input?.version===SCHEMA?validateWorkspace(input):migrateLegacy(input);}
export function layout(project,collapsed=new Set(),showArchived=false){
  const root=project.nodes.find(n=>n.type==='Project root'), visible=new Set([root.id]), byId=new Map(project.nodes.map(n=>[n.id,n]));
  let changed=true;while(changed){changed=false;for(const n of project.nodes){if(visible.has(n.id)||(!showArchived&&n.archived))continue;if(n.parents.some(id=>visible.has(id)&&!collapsed.has(id))){visible.add(n.id);changed=true;}}}
  const levels=new Map([[root.id,0]]);function level(n){if(levels.has(n.id))return levels.get(n.id);const l=1+Math.max(...n.parents.map(id=>level(byId.get(id))));levels.set(n.id,l);return l;}
  const rows=new Map();for(const n of project.nodes)if(visible.has(n.id)){const l=level(n);if(!rows.has(l))rows.set(l,[]);rows.get(l).push(n);}
  const width=Math.max(900,...[...rows.values()].map(row=>row.length*260+80));const positions=new Map();
  for(const [l,row] of rows)row.forEach((n,i)=>positions.set(n.id,{x:width/2-row.length*260/2+i*260+25,y:35+l*205}));
  return {nodes:project.nodes.filter(n=>visible.has(n.id)),positions,width,height:Math.max(560,(Math.max(...rows.keys())+1)*205+35)};
}
