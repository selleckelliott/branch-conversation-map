import {emptyWorkspace, makeProject, createBranch, validateWorkspace} from './model.mjs';
const uuid=()=>globalThis.crypto.randomUUID();
export function chatGPTEntries(input){const entries=Array.isArray(input)?input:[input];if(!entries.length||entries.some(c=>!c||typeof c.mapping!=='object'||!c.mapping))throw new Error('Choose a conversations.json export or a single exported conversation.');return entries;}
// Text is preserved; non-text attachments are represented explicitly, never silently omitted.
function textOf(message){const c=message.content||{};const parts=Array.isArray(c.parts)?c.parts:[];return parts.map(p=>typeof p==='string'?p:`[${p?.content_type||c.content_type||'Attachment'}: not imported]`).join('\n')||c.text||`[${c.content_type||'Non-text content'}: not imported]`;}
export function importChatGPT(input,indices){
  const entries=chatGPTEntries(input),state=emptyWorkspace();
  const selected=indices===undefined?entries:indices.map(i=>{if(!Number.isInteger(i)||i<0||i>=entries.length)throw new Error('Invalid conversation selection.');return entries[i]});
  if(!selected.length||selected.length>100)throw new Error('Select between 1 and 100 conversations per import.');
  for(const conv of selected){
    const p=makeProject(String(conv.title||'Imported conversation').slice(0,100));p.source={provider:'chatgpt',id:String(conv.id||conv.conversation_id||''),importedAt:new Date().toISOString()};state.projects.push(p);
    const mapping=conv.mapping,entries=Object.entries(mapping);if(entries.length>20000)throw new Error('Conversation is too large.');
    const children=new Map(),roots=[];
    for(const [id,n] of entries){if(n.parent===null||n.parent===undefined)roots.push(id);else{if(!Object.hasOwn(mapping,n.parent))throw new Error('Export contains a missing message parent.');if(!children.has(n.parent))children.set(n.parent,[]);children.get(n.parent).push(id);}}
    const seen=new Set();let branchNo=0;
    // Iterative traversal preserves actual parent links, including forks caused by edited prompts.
    const stack=roots.map(id=>({id,branch:p.nodes[0]})).reverse();
    while(stack.length){let {id,branch}=stack.pop();if(seen.has(id))throw new Error('Export contains a cycle or duplicate message.');seen.add(id);const item=mapping[id],message=item.message;
      if(message){const role=message.author?.role;if(role==='user'||role==='assistant'||role==='system'||role==='developer'||role==='tool'){
        if(branch.type==='Project root')branch=createBranch(p,{parentId:branch.id,title:conv.title?.slice(0,200)||'Imported conversation',summary:'Imported from ChatGPT'});
        branch.messages.push({id:uuid(),role:role==='user'?'You':role==='assistant'?'Assistant':role==='tool'?'Tool output':'Source instruction',text:String(textOf(message)),sourceMessageId:String(message.id||id),createdAt:message.create_time?new Date(message.create_time*1000).toISOString():new Date().toISOString()});
      }}
      const next=children.get(id)||[];
      for(let i=next.length-1;i>=0;i--){let target=branch;if(next.length>1&&branch.type!=='Project root'){branchNo++;target=createBranch(p,{parentId:branch.id,title:`Alternative ${branchNo}`,summary:'Branch preserved from the exported conversation'});}stack.push({id:next[i],branch:target});}
    }
    if(seen.size!==entries.length)throw new Error('Export contains an unreachable message or a cycle.');
  }
  return validateWorkspace(state);
}
