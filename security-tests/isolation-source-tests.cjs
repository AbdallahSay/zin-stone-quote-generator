const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const forbidden = ['5600249013','1284402479','factory-quote.runasp.net','factory-quote-frontend.vercel.app','db71745','zin-stone-api.onrender.com'];
function scan(dir) {
  for (const entry of fs.readdirSync(path.join(root,dir),{withFileTypes:true})) {
    if (['bin','obj','publish','.git'].includes(entry.name)) continue;
    const relative = path.join(dir,entry.name);
    if(entry.isDirectory()) scan(relative);
    else if(/\.(cs|json|js|html|ya?ml)$/.test(entry.name)) {
      const text=fs.readFileSync(path.join(root,relative),'utf8');
      for(const pattern of forbidden) assert(!text.includes(pattern),`${relative}: forbidden company identifier ${pattern}`);
    }
  }
}
for(const dir of ['backend','frontend','.github']) scan(dir);
const config=JSON.parse(fs.readFileSync(path.join(root,'backend/appsettings.json'),'utf8'));
assert.equal(config.Telegram.Enabled,false);
assert.equal(config.Telegram.OutboxWorkerEnabled,false);
assert.equal(config.WhatsApp.Enabled,false);
assert.equal(config.Telegram.ChatIds,'');
assert.equal(config.Security.SigningKey,'');
console.log('PASS: company identifier, notification default, and signing configuration guards');
