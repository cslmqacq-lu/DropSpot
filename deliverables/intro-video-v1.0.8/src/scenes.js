// =====================================================================
// 时间轴（秒）。每个场景按文案分镜排布。
// =====================================================================
const SUBS = [
  [0.3, 3.0, '报表导出了，然后呢？'],
  [3.2, 6.0, '它存哪儿了？'],
  [6.2, 8.0, '别翻了，看右下角。'],
  [8.3, 13.0, 'DropSpot，专门盯着你刚存的文件。'],
  [13.3, 19.0, '一保存就提醒，双击直达。'],
  [19.3, 26.0, '鼠标一放，最近的文件一眼看完。'],
  [26.3, 31.0, '找到了？直接拖去发。'],
  [31.3, 37.0, '常用的，拖进来就收藏。'],
  [37.3, 42.0, '快捷键，一键打开。'],
  [42.3, 47.0, '垃圾文件，自动藏好。'],
  [47.3, 52.0, '嫌碍眼？贴边藏起来。'],
  [52.3, 56.0, '刚改过的，马上找到；常用的，一点就到。'],
  [56.3, 60.0, 'DropSpot。以后找文件，看右下角就行。'],
];
window.DURATION = 60;

// ---------------- 背景缓慢漂移 ----------------
A('#bg', 0, 60, [{ transform: 'translate(0,0)' }, { transform: 'translate(-120px,60px)' }], { easing: 'linear' });

// =====================================================================
// S1–S3  0–8.2s  文件失踪小剧场
// =====================================================================
(function () {
  const s = scene(0, 8.25, 's1');
  const desk = add(s, `<div class="abs" style="left:0;top:0;width:1920px;height:1080px"></div>`);
  // 桌子 + 显示器
  add(desk, `<div class="abs" style="left:300px;top:742px;width:1320px;height:26px;border-radius:13px;background:#1E2A3D"></div>`);
  const mon = add(desk, `<div class="abs" style="left:800px;top:330px;width:600px;height:390px">
     <div class="abs" style="left:0;top:0;width:600px;height:360px;border-radius:22px;background:#1A2434;border:6px solid #2B3A52"></div>
     <div class="abs" style="left:270px;top:360px;width:60px;height:34px;background:#2B3A52"></div>
     <div class="abs" style="left:200px;top:384px;width:200px;height:14px;border-radius:7px;background:#2B3A52"></div>
     <div class="abs sheet" style="left:26px;top:26px;width:548px;height:290px;border-radius:10px;background:#F5F7FB;overflow:hidden">
       <div style="height:34px;background:#3DDC84;opacity:.85"></div>
       <div style="position:absolute;inset:34px 0 0 0;background-image:linear-gradient(#D7DEE8 1.5px,transparent 1.5px),linear-gradient(90deg,#D7DEE8 1.5px,transparent 1.5px);background-size:110px 36px"></div>
     </div>
     <div class="abs btn" style="left:430px;top:262px;width:126px;height:44px;border-radius:10px;background:#1F9D5A;color:#fff;font-size:22px;font-weight:700;display:flex;align-items:center;justify-content:center">导出</div>
   </div>`);
  const person = add(desk, personSvg('p1'), { left: '470px', top: '380px' });
  // 老板气泡
  const boss = add(desk, `<div class="abs" style="left:250px;top:170px;display:flex;align-items:center;gap:18px">
     <div style="width:86px;height:86px;border-radius:50%;background:#F59E6B;display:flex;align-items:center;justify-content:center;font-size:26px;font-weight:700;color:#3A1C0C">老板</div>
     <div style="padding:16px 26px;border-radius:24px 24px 24px 6px;background:#fff;color:#1D2633;font-size:30px;font-weight:700">把刚才那份报表发我</div></div>`);
  riseIn(boss, 0.15, .5, 30);
  // 鼠标点导出
  const cur = add(desk, `<div class="abs">${cursorSvg()}</div>`, { left: '0', top: '0' });
  A(cur, 0.6, .7, [{ transform: 'translate(1150px,900px)', opacity: 0 }, { transform: 'translate(1290px,610px)', opacity: 1 }]);
  A(mon.querySelector('.btn'), 1.35, .25, [{ transform: 'scale(1)' }, { transform: 'scale(.88)', offset: .5 }, { transform: 'scale(1)' }]);
  fadeOut(cur, 2.2);
  // 报表图标弹出后“嗖”地飞进电脑
  const xl = add(desk, `<div class="abs">${fileSvg('XLSX')}</div>`, { left: '0', top: '0' });
  A(xl, 1.5, 1.1, [
    { transform: 'translate(1250px,560px) scale(.3) rotate(0deg)', opacity: 0 },
    { transform: 'translate(1290px,300px) scale(1.15) rotate(-12deg)', opacity: 1, offset: .35 },
    { transform: 'translate(1100px,250px) scale(1) rotate(8deg)', opacity: 1, offset: .55 },
    { transform: 'translate(1050px,470px) scale(.05) rotate(360deg)', opacity: 0 }], { easing: EASE_IO });

  // S2 文件夹一个个翻，全是空的
  const spots = [[250, 300, '桌面'], [1480, 230, '下载'], [1520, 560, '文档'], [230, 560, '我的文件夹']];
  spots.forEach(([x, y, n], i) => {
    const c = add(desk, `<div class="abs" style="left:${x}px;top:${y}px;width:220px;height:190px;border-radius:24px;background:#141D2A;border:2px solid #2B3A52;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:10px">
       ${folderSvg()}<div style="font-size:26px;font-weight:700">${n}</div><div class="empty" style="font-size:20px;color:#F87171">空空如也</div></div>`);
    c.querySelector('.fold').style.cssText = 'width:96px;height:80px';
    popIn(c, 3.15 + i * .3);
    A(c.querySelector('.fold'), 3.45 + i * .3, .4, [{ transform: 'rotate(0)' }, { transform: 'rotate(-14deg)', offset: .4 }, { transform: 'rotate(8deg)', offset: .7 }, { transform: 'rotate(0)' }]);
    fadeIn(c.querySelector('.empty'), 3.55 + i * .3, .2);
  });
  const hl = add(desk, `<div class="headline">桌面？下载？文档？</div>`);
  riseIn(hl, 3.3);
  fadeOut(hl, 5.9);
  // 小人慌了：举手 + 冒汗
  A(person.querySelector('.armL'), 4.4, .5, [{ transform: 'rotate(0)' }, { transform: 'rotate(55deg)' }], { easing: EASE_BACK });
  A(person.querySelector('.armR'), 4.4, .5, [{ transform: 'rotate(0)' }, { transform: 'rotate(-55deg)' }], { easing: EASE_BACK });
  A(person.querySelector('.sweat'), 4.6, .3, [{ opacity: 0 }, { opacity: 1 }]);
  A(person.querySelector('.mouth'), 4.4, .2, [{ d: 'path("M134 192 Q150 202 166 192")' }, { d: 'path("M134 198 Q150 186 166 198")' }]);
  // 文件夹越堆越高
  for (let i = 0; i < 12; i++) {
    const f = add(desk, `<div class="abs">${folderSvg()}</div>`, { left: '0', top: '0' });
    f.firstElementChild.style.cssText = 'width:110px;height:92px';
    const x = 470 + (i % 4) * 62 + (i * 37 % 40), y = 650 - Math.floor(i / 4) * 70 - (i % 2) * 12, r = (i * 47 % 50) - 25;
    A(f, 4.7 + i * .08, .45, [{ transform: `translate(${x}px,-150px) rotate(${r * 3}deg)`, opacity: 1 }, { transform: `translate(${x}px,${y}px) rotate(${r}deg)`, opacity: 1 }], { easing: 'cubic-bezier(.5,0,.75,1.25)' });
  }
  // 报表在右下角偷笑
  const peek = add(desk, `<div class="abs">${fileSvg('XLSX', 110, true)}</div>`, { left: '0', top: '0' });
  A(peek, 5.2, .5, [{ transform: 'translate(1720px,1100px) rotate(-14deg)' }, { transform: 'translate(1720px,930px) rotate(-14deg)' }], { easing: EASE_BACK });
  A(peek, 6.0, .3, [{ transform: 'translate(1720px,930px) rotate(-14deg)' }, { transform: 'translate(1720px,1120px) rotate(-14deg)' }]);

  // S3 画面变灰，右下角绿点 + 悬浮舱提示弹入
  A(desk, 6.0, .4, [{ filter: 'grayscale(0) brightness(1)' }, { filter: 'grayscale(1) brightness(.45)' }]);
  const pdot = add(s, `<div class="abs" style="left:1792px;top:880px;width:28px;height:28px;border-radius:50%;background:#3DDC84;box-shadow:0 0 40px #3DDC84"></div>`);
  popIn(pdot, 6.05, .4);
  const ring = add(s, `<div class="abs" style="left:1766px;top:854px;width:80px;height:80px;border-radius:50%;border:4px solid #3DDC84"></div>`);
  A(ring, 6.15, .9, [{ transform: 'scale(.3)', opacity: 0 }, { transform: 'scale(.4)', opacity: 1, offset: .05 }, { transform: 'scale(2.2)', opacity: 0 }], { easing: 'ease-out' });
  const t = add(s, toastCard('报表.xlsx', '财务'), { left: '1240px', top: '650px' });
  A(t, 6.45, .6, [{ transform: 'translate(560px,240px) scale(.2)', opacity: 0 }, { transform: 'translate(0,0) scale(1)', opacity: 1 }], { easing: EASE_BACK });
  fadeOut(pdot, 6.5, .2);
  const hl2 = add(s, `<div class="headline green" style="font-size:96px;top:330px">还真有！</div>`);
  popIn(hl2, 6.6);
})();
