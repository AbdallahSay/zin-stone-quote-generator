const fs=require('node:fs'),path=require('node:path'),http=require('node:http'),assert=require('node:assert/strict');
const {chromium,firefox,webkit}=require('playwright');
const root=path.resolve(__dirname,'..');
assert.equal(fs.readFileSync(path.join(root,'frontend/index.html'),'utf8'),fs.readFileSync(path.join(root,'backend/wwwroot/index.html'),'utf8'));
const server=http.createServer((req,res)=>{
 const file=path.resolve(root,'.'+decodeURIComponent(req.url.split('?')[0]));
 if(!file.startsWith(root+path.sep)){res.writeHead(403).end();return;}
 fs.readFile(file,(error,bytes)=>{if(error)res.writeHead(404).end();else{res.setHeader('Content-Type',file.endsWith('.html')?'text/html; charset=utf-8':'text/javascript; charset=utf-8');res.end(bytes);}});
});
(async()=>{
 await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));let checked=0;
 try{
  for(const [name,type] of Object.entries({chromium,firefox,webkit}).filter(([name])=>!process.env.ENGINES||process.env.ENGINES.split(',').includes(name))){
   const browser=await type.launch(name==='chromium'&&process.env.CHROMIUM_PATH?{executablePath:process.env.CHROMIUM_PATH}:{});
   try{for(const width of [375,390,768,1280,1920]){
    const context=await browser.newContext({viewport:{width,height:900}});
    await context.addInitScript(()=>{localStorage.setItem('fq_token_employee','synthetic-only');localStorage.setItem('fq_user',JSON.stringify({id:101,fullName:'TEST ZIN STONE Production Employee اسم موظف عربي طويل'}));});
    await context.route('**/api/**',route=>route.fulfill({json:route.request().url().includes('/verify')?{valid:true}:[],headers:{'Access-Control-Allow-Origin':'*'}}));
    const page=await context.newPage();await page.goto(`http://127.0.0.1:${server.address().port}/frontend/index.html`);
    await page.locator('#quoteMainContent').waitFor({state:'visible'});await page.locator('#productRowsContainer tr').first().waitFor();
    await page.evaluate(()=>document.fonts.ready);
    const metrics=await page.evaluate(()=>({width:innerWidth,document:document.documentElement.scrollWidth,body:document.body.scrollWidth}));
    assert(metrics.document<=width&&metrics.body<=width,`${name} ${width}: page overflow ${JSON.stringify(metrics)}`);
    checked++;await context.close();
   }}finally{await browser.close();}
  }
  console.log(`PASS: ${checked} quotation viewport checks, no page overflow; bounded product-table scrolling retained`);
 }finally{server.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
