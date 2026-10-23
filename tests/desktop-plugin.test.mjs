import test from 'node:test';
import assert from 'node:assert/strict';
import {handleDesktopRPC} from '../desktop/plugin.mjs';
test('desktop bridge exposes native launcher tools without local HTTP dependencies',async()=>{
 const initialized=await handleDesktopRPC({id:1,method:'initialize'});assert.equal(initialized.result.serverInfo.name,'branch-desktop');
 const listed=await handleDesktopRPC({id:2,method:'tools/list'});assert.deepEqual(listed.result.tools.map(t=>t.name),['branch_open_desktop_map','branch_desktop_status']);
 const status=await handleDesktopRPC({id:3,method:'tools/call',params:{name:'branch_desktop_status'}});const data=JSON.parse(status.result.content[0].text);assert.equal(typeof data.built,'boolean');assert.ok(data.executable.endsWith('Branch.Desktop.exe'));
 assert.equal(await handleDesktopRPC({method:'notifications/initialized'}),null);
 const unknown=await handleDesktopRPC({id:4,method:'tools/call',params:{name:'other'}});assert.equal(unknown.result.isError,true);
});
