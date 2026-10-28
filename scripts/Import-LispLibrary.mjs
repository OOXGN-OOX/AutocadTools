import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const project=path.resolve(import.meta.dirname,'..');
const source=process.argv[2] || 'C:/acad_pro';
const output=path.join(project,'artifacts/library-source');
if(!output.startsWith(path.join(project,'artifacts')+path.sep))throw Error('Invalid staging directory');
fs.rmSync(output,{recursive:true,force:true});
fs.mkdirSync(output,{recursive:true});
const descriptions=JSON.parse(fs.readFileSync(path.join(project,'Library/function-descriptions.json'),'utf8').replace(/^\uFEFF/,''));
function walk(dir){return fs.readdirSync(dir,{withFileTypes:true}).flatMap(e=>e.isDirectory()?walk(path.join(dir,e.name)):[path.join(dir,e.name)]);}
function decode(bytes){if(bytes[0]===255&&bytes[1]===254)return new TextDecoder('utf-16le').decode(bytes);try{return new TextDecoder('utf8',{fatal:true}).decode(bytes)}catch{return new TextDecoder('windows-1251').decode(bytes)}}
function strip(source){let out='',quoted=false,escaped=false,line=false,block=false;for(let i=0;i<source.length;i++){let c=source[i];if(block){if(c==='|'&&source[i+1]===';'){block=false;i++}out+=' ';continue}if(line){if(c==='\r'||c==='\n')line=false;out+=c==='\n'?'\n':' ';continue}if(quoted){if(!escaped&&c==='"')quoted=false;escaped=!escaped&&c==='\\';out+=' ';continue}if(c===';'){if(source[i+1]==='|'){block=true;i++}else line=true;out+=' ';continue}if(c==='"'){quoted=true;out+=' ';continue}out+=c}return out}
const files=walk(source), sources=files.filter(p=>/\.(lsp|fas|vlx|mnl)$/i.test(p));
const texts=sources.filter(p=>/\.(lsp|mnl)$/i.test(p)).map(p=>decode(fs.readFileSync(p))).join('\n');
const keep=new Set(['.lsp','.fas','.vlx','.mnl','.dcl','.slb','.sld','.bmp','.png','.lin','.pat','.cfg','.ini','.txt','.htm','.html','.shx','.shp']);
let copied=0,rewritten=0;
for(const file of files){
 const ext=path.extname(file).toLowerCase();
 if(!keep.has(ext)&&!(ext==='.dwg'&&texts.toLowerCase().includes(path.basename(file).toLowerCase())))continue;
 const relative=path.relative(source,file), dest=path.join(output,relative);fs.mkdirSync(path.dirname(dest),{recursive:true});
 const normalizedRelative=relative.replaceAll('\\','/');
 if(!/\.(lsp|fas|vlx|mnl|dwg)$/i.test(file)&&! /^(?:acad_pro архив\/)?(?:Lisp|acad_pan|Fonts|Lin)\//i.test(normalizedRelative))continue;
 if(ext==='.txt'&&!/(?:readme|license|licence|copying|notice|history|help|change|пояснен|описан|инструк)/i.test(path.basename(file)))continue;
 let bytes=fs.readFileSync(file);
 if(/\.(lsp|mnl)$/i.test(file)){
  let text=decode(bytes), changed=false;
  text=text.replace(/"((?:[^"\\]|\\.)*)"/g,(whole,value)=>{
   const normalized=value.replace(/\\\\/g,'/').replace(/\\/g,'/');
   if(!/^c:\/acad_pro(?:\/|$)/i.test(normalized))return whole;
   changed=true;return '(strcat botplus:library-root '+JSON.stringify(normalized.slice('C:/acad_pro'.length))+')';
  });
  if(changed){bytes=Buffer.from(text,'utf8');rewritten++;}
 }
 fs.writeFileSync(dest,bytes);copied++;
}
const menus=files.filter(p=>/\.(mnu|mns)$/i.test(p)).flatMap(p=>decode(fs.readFileSync(p)).split(/\r?\n/)).filter(l=>!/^\s*\/\//.test(l));
const overrides={
 'pl-vfi':'Добавить вершины полилиний в местах пересечений','rl':'Развернуть направление отрезков',
 'bgcolorcmd':'Изменить цвет всех объектов по номеру ACI','bgcolor':'Изменить цвет всех объектов через палитру',
 'bgbldyn2a':'Сделать выбранные динамические блоки анонимными','bgbldyn2s':'Сделать выбранные динамические блоки статическими',
 'bgblalldyn2a':'Сделать все динамические блоки анонимными','bgblalldyn2s':'Сделать все динамические блоки статическими',
 'bgblxclipexp':'Расчленить блок с учётом границы подрезки','bgblcc':'Изменить цвет содержимого блоков','bgencc':'Изменить цвет вложенных объектов блока',
 'bgblfix':'Исправить свойства объектов внутри блоков','bglaydel':'Удалить выключенные и замороженные слои с объектами',
 'bgcatt':'Изменить цвет атрибутов блоков','bgcbl2':'Изменить цвет блоков и их содержимого','bg1blexp':'Расчленить блоки из одного объекта',
 'bgblxclip':'Выбрать блоки с подрезкой','bgblexp1noatt':'Расчленить блоки без сохранения атрибутов','bgbldynexp1':'Расчленить динамические блоки',
 'bgblexp1':'Расчленить блоки на один уровень','bgblexp':'Рекурсивно расчленить блоки','bgcftsel':'Преобразовать выбранные поля в обычный текст','bgcft':'Преобразовать поля чертежа в обычный текст',
 'appdel':'Удалить расширенные данные приложений','ux':'Преобразовать анонимные блоки в именованные','m2b':'Преобразовать многократные вставки в блоки',
 'u2b':'Переименовать анонимный блок','u2bm':'Переименовать несколько анонимных блоков','m2u':'Преобразовать многократные вставки в анонимные блоки',
 'bgcolorxref':'Изменить цвет внешних ссылок в текущем сеансе','bghatchdel':'Удалить штриховки','bgrgb2aci':'Преобразовать цвета RGB в индексные ACI',
 'bgresetxref':'Сбросить свойства слоёв внешних ссылок','bgall2rgb':'Преобразовать индексные цвета в RGB','bgcb':'Создать копию блока с новым именем','bgrb':'Переименовать блок','bgcolbl':'Перенести явно заданные цвета объектов на слои',
 'подписи':'Вставить подписи проекта','роза':'Вставить розу ветров','обозначения':'Вставить условные обозначения','миниатюры':'Вставить миниатюры','опора_бк':'Вставить опору БК','опора_пл':'Вставить опору ПЛ','пересечения':'Вставить обозначение пересечения',
 'cft':'Преобразовать все поля в текст','cftall':'Преобразовать все поля в текст','cftsel':'Преобразовать выбранные поля в текст',
 'cts':'Привести текстовые стили к общим параметрам','ctsu':'Обновить текстовые стили','unf-mtext':'Убрать оформление многострочного текста','tsup':'Удалить неиспользуемые текстовые стили',
 'coor':'Экспортировать координаты точек','coorn':'Экспортировать пронумерованные координаты','coornp':'Экспортировать координаты с маркерами','coornb':'Экспортировать координаты блоков','coort':'Экспортировать координаты текста','coorvik':'Экспортировать координаты трассы','hvacrep':'Создать отчёт по трассе',
 'at':'Создать марки площадей и таблицу','af':'Экспортировать ведомость площадей в файл',
 'mvatt':'Переместить атрибуты','roatt':'Повернуть атрибуты','edatt':'Изменить свойства атрибутов',
 'cwipe':'Создать круглую маскировку','c2wipe':'Заменить круги маскировками','c2l':'Скопировать объекты на выбранные листы','c2al':'Скопировать объекты на все листы',
 'count':'Подсчитать блоки и создать ведомость','countsettings':'Настроить ведомость подсчёта блоков',
 'incarray':'Создать массив с увеличением номеров','incarrayd':'Создать динамический массив с увеличением номеров',
 'brk':'Разделить кривую и оформить скрытый участок','brko':'Разделить кривую с сохранением исходника',
 'selcounton':'Включить счётчик предварительного выбора','selcountoff':'Выключить счётчик предварительного выбора',
 'tc':'Вычислить выражение по числам в надписях','textcalc':'Вычислить выражение по числам в надписях',
 'stripmtext':'Снять выбранное оформление текста','smt':'Снять выбранное оформление текста'
};
const entries=[],seen=new Set();
const ordered=sources.filter(p=>!p.toLowerCase().endsWith('.mnl')).sort((a,b)=>Number(a.includes('архив'))-Number(b.includes('архив'))||a.length-b.length);
for(const file of ordered){
 const bytes=fs.readFileSync(file),hash=crypto.createHash('sha256').update(bytes).digest('hex');if(seen.has(hash))continue;seen.add(hash);
 const relative=path.relative(source,file).replaceAll('\\','/');
 const info=descriptions.find(d=>d.sha256===hash)||descriptions.find(d=>d.path.replaceAll('\\','/').toLowerCase()===relative.toLowerCase());
 if(!info)continue;
 const code=strip(decode(bytes));
 const live=new Set([...code.matchAll(/\(\s*defun(?:-q)?\s+c:([^\s()"';]+)/gi)].map(m=>m[1].toLowerCase()));
 const declared=info.commands.filter(c=>live.has(c.toLowerCase()));
 const commands=declared.length?declared:[null];
 for(const command of commands){
  let title=command&&overrides[command.toLowerCase()];
  if(!title&&command){
   const escaped=command.replace(/[.*+?^${}()|[\]\\]/g,'\\$&');
   const menu=menus.find(l=>/\[_Button\("/.test(l)&&new RegExp('(?:\\^P|[;\\s])'+escaped+'(?:[;\\s]|$)','i').test(l));
   if(menu)title=menu.match(/\[_Button\("([^"]+)"/)[1];
  }
  title??=info.title.replace(/^(?:Pltools|Bgtools|CTS|BINC|CopyField|IncArray|ECoorE?|StripMtext|NumInc|BFind|ESConvert)(?:\s+[\w.\-]+)?\s*:\s*/i,'');
  if(/inc-dec\.lsp$/i.test(file))title='Загрузить увеличение и уменьшение числовых значений';
  title=title[0].toUpperCase()+title.slice(1);
  let unavailable=null;
  if(/повреждённый/.test(info.title))unavailable='Исходник повреждён; используйте исправный вариант автонумерации атрибутов.';
  if(command==='DYN_TEST')unavailable='Для этой демонстрации требуется отдельный ARX-модуль соответствующей версии AutoCAD.';
  if(command==='Number')unavailable='Требуется внешний VBA-макрос Number, которого нет в библиотеке.';
  entries.push({File:relative,Command:command,Symbol:command?'c:'+command:null,Title:title,Group:info.group,Description:info.result+' '+info.limits,UnavailableReason:unavailable});
 }
}
const compiled=[
 ['Move-to-visibilityset.fas','Скопировать элементы в состояния видимости','Динамические блоки',null],
 ['Geo_Otkos_v3_3.VLX','Нарисовать геодезические откосы','Геодезия','c:Geo_Otkos'],
 ['Geo_put_dist_by_poly.VLX','Проставить размеры по полилинии','Геодезия','c:Geo_put_dist_by_poly'],
 ['b2l.VLX','Загрузить инструменты преобразования блоков','Блоки',null],
 ['BlockToPline04.fas','Загрузить преобразование блока в полилинию','Блоки',null],
 ['myOrder.fas','Загрузить расширенную автонумерацию','Нумерация',null],
 ['select_insert_by_attr_filter_ru.fas','Загрузить выбор блоков по фильтру атрибутов','Атрибуты',null],
 ['txthand.fas','Загрузить инструменты работы с текстом','Текст',null],
 ['Simple_Vent_1.1.VLX','Загрузить инструменты вентиляции','Вентиляция',null]
];
for(const [name,title,group,symbol]of compiled){const file=ordered.find(p=>path.basename(p).toLowerCase()===name.toLowerCase());if(!file)continue;entries.push({File:path.relative(source,file).replaceAll('\\','/'),Command:null,Symbol:symbol,Title:title,Group:group,Description:symbol?'Команда из исходного меню библиотеки.':'Загрузка исходной библиотеки; команды предоставляются её интерфейсом.'});}
const visibility=ordered.find(p=>path.basename(p).toLowerCase()==='visibility-add2.fas');
if(visibility)for(const line of menus.filter(l=>/load "Visibility-ADD2.fas"/i.test(l))){const title=line.match(/\[_Button\("([^"]+)"/)?.[1],symbol=line.match(/\.fas"\)\s*\)\s*\(([^\s()]+)/i)?.[1]||line.match(/\.fas"\)\s*\(([^\s()]+)/i)?.[1];if(title&&symbol)entries.push({File:path.relative(source,visibility).replaceAll('\\','/'),Title:title,Group:'Динамические блоки',Symbol:symbol,Description:'Управление состояниями видимости динамического блока.'});}
function score(e){return (!e.File.includes('архив')?200:0)+(e.File.includes('3.11')?100:0)+(e.File.includes('1-8')?80:0)+(e.File.includes('1-1')?30:0)-(e.UnavailableReason?1000:0)}
const unique=[];for(const e of entries.sort((a,b)=>score(b)-score(a))){const key=(e.Symbol||e.Title).toLowerCase();if(unique.some(x=>(x.Symbol||x.Title).toLowerCase()===key))continue;if(unique.some(x=>x.Title===e.Title&&x.File===e.File))continue;unique.push(e);}
fs.writeFileSync(path.join(output,'catalog.json'),JSON.stringify(unique,null,2)+'\n');
fs.writeFileSync(path.join(output,'SOURCE-NOTICE.txt'),'Additional AutoLISP library imported from C:\\acad_pro. Original author and license notices are retained in the source files. Only paths into the original library directory were adapted to botplus:library-root.\n');
console.log(JSON.stringify({copied,rewritten,functions:unique.length,output}));
