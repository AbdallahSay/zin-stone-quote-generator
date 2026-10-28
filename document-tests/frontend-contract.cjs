const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=path.join(__dirname,'..');
for(const file of ['frontend/index.html','backend/wwwroot/index.html']){
 const html=fs.readFileSync(path.join(root,file),'utf8');
 const select=html.match(/<select id="contactTitle"[\s\S]*?<\/select>/)[0];
 for(const title of ['المهندس','المهندسة','الأستاذ','الأستاذة','السيد','السيدة','الدكتور','الدكتورة'])assert(select.includes(`value="${title}"`),title);
 assert(/contactTitle:\s*contactTitle/.test(html));
 const start=html.indexOf('const cleanAttachmentPart =');
 const end=html.indexOf("const pdfShareFileName =",start);
 const stop=html.indexOf(';',end)+1;
 assert(start>0&&end>start&&stop>end);
 const code=html.slice(start,stop);
 for(const [clientName,projectName,expected] of [['شركة الأفق','مشروع الساحل','عرض سعر شركة الأفق مشروع الساحل.pdf'],['الأفق','','عرض سعر شركة الأفق.pdf'],['شركة النور/الغد','مدينة النور: المرحلة الأولى','عرض سعر شركة النور الغد مشروع مدينة النور المرحلة الأولى.pdf']]){
  const actual=vm.runInNewContext(`(()=>{${code};return new File(['test'],pdfShareFileName,{type:'application/pdf'}).name})()`,{clientName,projectName,File});
  assert.equal(actual,expected);
 }
 assert(html.includes('new File([blob], pdfShareFileName'));
 console.log('PASS: '+file+' eight-title request contract and Arabic mobile File names');
}
