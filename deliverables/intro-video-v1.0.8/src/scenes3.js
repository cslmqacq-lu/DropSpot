function keyRow(parent, y, keys, t0) {
  let x = 150; const els = [];
  keys.forEach((k, i) => {
    const w = k.length > 1 ? 150 : 112;
    const el = add(parent, `<div class="key" style="left:${x}px;top:${y}px;width:${w}px">${k}</div>`);
    popIn(el, t0 - .45 + i * .08, .4);
    A(el, t0 + i * .2, .3, [{ transform: 'translateY(0)', background: '#1E2A3D', color: '#DCE6F2' },
      { transform: 'translateY(8px)', background: '#1F9D5A', color: '#fff', offset: .35 },
      { transform: 'translateY(8px)', background: '#1F9D5A', color: '#fff', offset: .8 },
      { transform: 'translateY(0)', background: '#1E2A3D', color: '#DCE6F2' }]);
    els.push(el); x += w;
    if (i < keys.length - 1) { const pl = add(parent, `<div class="plus" style="left:${x + 14}px;top:${y + 18}px">+</div>`); fadeIn(pl, t0 - .4 + i * .08, .3); x += 56; }
  });
  return els;
}

// =====================================================================
// S9  37–42.2  全局快捷键
// =====================================================================
(function () {
  const s = scene(37.0, 42.25, 's9');
  keyRow(s, 230, ['Ctrl', 'Alt', 'F'], 37.5);
  const l1 = add(s, `<div class="lbl" style="left:150px;top:370px">打开<span class="green">最新文件夹</span></div>`);
  riseIn(l1, 38.2);
  const ex = add(s, explorerHtml(760, 330, [
    ['XLSX', '报表.xlsx', '2026/10/1 09:41', 'Excel 工作表'],
    ['PDF', '发票汇总.pdf', '2026/9/27 11:20', 'PDF 文件'],
    ['DOCX', '会议纪要.docx', '2026/9/25 15:48', 'Word 文档']], 0), { left: '1010px', top: '150px' });
  A(ex, 38.15, .5, [{ transform: 'scale(.6)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], { easing: EASE_BACK });
  A(ex.querySelector('.r.sel'), 38.6, .4, [{ background: 'rgba(61,220,132,0)' }, { background: 'rgba(61,220,132,.18)' }]);

  keyRow(s, 590, ['Ctrl', 'Alt', 'D'], 39.6);
  const l2 = add(s, `<div class="lbl" style="left:150px;top:730px"><span class="green">复制</span>文件夹路径</div>`);
  riseIn(l2, 40.2);
  const dlg = add(s, `<div class="win" style="left:1010px;top:560px;width:760px;height:250px">
     <div class="tb">上传文件<div class="ctl"><span>✕</span></div></div>
     <div style="padding:22px 28px;font-size:21px;color:#4A5668">文件夹地址</div>
     <div class="addr" style="margin:0 28px;height:56px;font-size:24px;border-color:#3DDC84"><span class="pth" style="color:#1D2633;font-weight:700">D:\\工作\\财务</span></div>
     <div style="position:absolute;right:28px;bottom:22px;padding:8px 28px;border-radius:8px;background:#1F9D5A;color:#fff;font-size:20px">打开</div></div>`);
  A(dlg, 39.3, .5, [{ transform: 'scale(.6)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], { easing: EASE_BACK });
  const pth = dlg.querySelector('.pth');
  A(pth, 40.85, .2, [{ opacity: 0 }, { opacity: 1 }]);
  const fly = add(s, `<div class="pill" style="left:0;top:0;color:#3DDC84;border-color:#3DDC84">D:\\工作\\财务</div>`);
  A(fly, 40.2, .7, [{ transform: 'translate(620px,610px) scale(.6)', opacity: 0 }, { transform: 'translate(800px,560px) scale(1)', opacity: 1, offset: .35 }, { transform: 'translate(1050px,690px) scale(.8)', opacity: 0 }], { easing: EASE_IO });
})();

// =====================================================================
// S10  42–47.2  垃圾文件过滤
// =====================================================================
(function () {
  const s = scene(42.0, 47.25, 's10');
  const hl = add(s, `<div class="headline">自动<span class="green">隐藏</span></div>`);
  riseIn(hl, 42.2);
  const chips = ['浏览器缓存', '日志', '临时文件', '编译产物'];
  let cx = 960 - (chips.length * 210 + (chips.length - 1) * 20) / 2;
  chips.forEach((c, i) => {
    const el = add(s, `<div class="pill" style="left:${cx}px;top:190px;width:210px;text-align:center;color:#9AA8BA"><span style="color:#F87171">✕</span> ${c}</div>`);
    popIn(el, 42.6 + i * .15, .45); cx += 230;
  });
  const net = add(s, `<div class="abs" style="left:930px;top:330px;width:34px;height:560px;border-radius:17px;border:3px solid #3DDC84;background:repeating-linear-gradient(0deg, rgba(61,220,132,.55) 0 3px, transparent 3px 22px), repeating-linear-gradient(90deg, rgba(61,220,132,.55) 0 3px, transparent 3px 11px);box-shadow:0 0 40px rgba(61,220,132,.45)"></div>`);
  A(net, 42.4, .5, [{ transform: 'scaleY(0)', opacity: 0 }, { transform: 'scaleY(1)', opacity: 1 }], { easing: EASE_BACK });
  const list = add(s, `<div class="cap" style="left:1150px;top:330px;width:600px;height:500px;border-radius:30px;padding:26px 30px">
      <div style="font-size:24px;color:#6F8199;margin-bottom:10px">最新文件</div><div class="lr" style="position:relative"></div></div>`);
  fadeIn(list, 42.5);
  const lr = list.querySelector('.lr');
  const items = [['LOG', 0], ['XLSX', 1, '报表.xlsx'], ['TMP', 0], ['PPTX', 1, '季度汇报.pptx'], ['CACHE', 0], ['PDF', 1, '合同.pdf'], ['OBJ', 0], ['JPG', 1, '现场照片.jpg']];
  const lanes = [380, 520, 660, 440, 760, 580, 410, 700];
  let row = 0;
  items.forEach(([ext, good, name], i) => {
    const t = 43.2 + i * .42, y = lanes[i];
    const ic = add(s, `<div class="abs">${fileSvg(ext, 80)}</div>`, { left: '0', top: '0' });
    if (!good) {
      A(ic, t, .75, [{ transform: `translate(-120px,${y}px) rotate(0)`, opacity: 1 }, { transform: `translate(840px,${y}px) rotate(8deg)`, opacity: 1 }], { easing: 'cubic-bezier(.4,0,.9,.6)' });
      A(ic, t + .75, .55, [{ transform: `translate(840px,${y}px) rotate(8deg)`, opacity: 1 }, { transform: `translate(620px,${y + 140}px) rotate(-70deg)`, opacity: 0 }], { easing: 'ease-out' });
      A(net, t + .72, .25, [{ boxShadow: '0 0 40px rgba(61,220,132,.45)' }, { boxShadow: '0 0 80px rgba(248,113,113,.9)' }, { boxShadow: '0 0 40px rgba(61,220,132,.45)' }]);
    } else {
      const ry = row * 104;
      const r = add(lr, `<div class="row" style="top:${ry}px;left:0;right:0">${badge(ext)}<div><div class="n">${name}</div><div class="m">刚刚</div></div></div>`);
      A(ic, t, 1.15, [{ transform: `translate(-120px,${y}px) scale(1)`, opacity: 1 }, { transform: `translate(1180px,${410 + ry}px) scale(.45)`, opacity: 1, offset: .9 }, { transform: `translate(1190px,${410 + ry}px) scale(.3)`, opacity: 0 }], { easing: 'cubic-bezier(.45,0,.4,1)' });
      A(r, t + 1.05, .4, [{ opacity: 0, transform: 'translateX(30px)' }, { opacity: 1, transform: 'translateX(0)' }]);
      row++;
    }
  });
})();

// =====================================================================
// S11  47–52.2  贴边隐藏 + 半透明
// =====================================================================
(function () {
  const s = scene(47.0, 52.25, 's11');
  const fr = add(s, `<div class="abs" style="left:210px;top:250px;width:1500px;height:660px;border-radius:26px;overflow:hidden;box-shadow:0 30px 90px rgba(0,0,0,.5);
     background:radial-gradient(600px 400px at 20% 30%, #3E7CB1 0%, transparent 70%), radial-gradient(700px 500px at 80% 70%, #8E5BBF 0%, transparent 70%), radial-gradient(500px 400px at 60% 10%, #2FA58A 0%, transparent 70%), #24314A"></div>`);
  fadeIn(fr, 47.0, .3);
  ['此电脑', '回收站', '项目'].forEach((n, i) => add(fr, `<div class="abs" style="left:34px;top:${30 + i * 130}px;width:110px;text-align:center;color:#fff;font-size:19px">${folderSvg()}<div>${n}</div></div>`).querySelector('.fold').style.cssText = 'width:70px;height:58px');
  // A：拖到边缘贴边
  const card = add(s, restCard('财务'), { left: '0', top: '0' });
  const cur = add(s, `<div class="abs">${cursorSvg()}</div>`, { left: '0', top: '0' });
  A(card, 47.2, 1.1, [{ transform: 'translate(1000px,420px)' }, { transform: 'translate(1000px,420px)', offset: .15 }, { transform: 'translate(1505px,420px)' }], { easing: EASE_IO });
  A(cur, 47.1, 1.2, [{ transform: 'translate(1120px,560px)', opacity: 0 }, { transform: 'translate(1110px,520px)', opacity: 1, offset: .2 }, { transform: 'translate(1615px,520px)', opacity: 1 }], { easing: EASE_IO });
  A(card, 48.35, .25, [{ opacity: 1, transform: 'translate(1505px,420px) scaleX(1)' }, { opacity: 0, transform: 'translate(1600px,420px) scaleX(.1)' }], { easing: EASE_IN });
  fadeOut(cur, 48.4, .2);
  const strip = add(s, `<div class="cap edge"><div class="dot"></div><div class="bar"></div></div>`, { left: '1682px', top: '452px' });
  A(strip, 48.5, .45, [{ transform: 'translateX(40px)', opacity: 0 }, { transform: 'translateX(0)', opacity: 1 }], { easing: EASE_BACK });
  A(strip, 49.0, .9, [{ boxShadow: '0 0 0 rgba(61,220,132,0)' }, { boxShadow: '0 0 36px rgba(61,220,132,.8)' }, { boxShadow: '0 0 0 rgba(61,220,132,0)' }], { iterations: 3 });
  const hl = add(s, `<div class="headline">拖到边缘 → <span class="green">贴边隐藏</span></div>`);
  riseIn(hl, 47.5);
  // B：背景透明度
  const c2 = add(s, restCard('财务'), { left: '560px', top: '380px' });
  popIn(c2, 49.4, .5);
  A(c2, 49.9, 1.7, [{ backgroundColor: 'rgba(16,23,34,1)' }, { backgroundColor: 'rgba(16,23,34,.5)', offset: .45 }, { backgroundColor: 'rgba(16,23,34,.5)', offset: .6 }, { backgroundColor: 'rgba(16,23,34,.8)' }], { easing: 'ease-in-out' });
  const sl = add(s, `<div class="abs" style="left:420px;top:640px;width:500px;height:90px;border-radius:20px;background:rgba(16,23,34,.92);border:2px solid #2A3648;padding:16px 26px">
      <div style="font-size:21px;color:#8A9BB0">背景不透明度</div>
      <div class="abs" style="left:26px;right:26px;top:60px;height:6px;border-radius:3px;background:#30405A"></div>
      <div class="abs fill" style="left:26px;width:448px;top:60px;height:6px;border-radius:3px;background:#3DDC84;transform-origin:left center"></div>
      <div class="abs knob" style="left:${26 + 448 - 13}px;top:50px;width:26px;height:26px;border-radius:50%;background:#E6EDF5"></div></div>`);
  riseIn(sl, 49.5, .45, 20);
  A(sl.querySelector('.fill'), 49.9, 1.7, [{ transform: 'scaleX(1)' }, { transform: 'scaleX(.05)', offset: .45 }, { transform: 'scaleX(.05)', offset: .6 }, { transform: 'scaleX(.62)' }], { easing: 'ease-in-out' });
  A(sl.querySelector('.knob'), 49.9, 1.7, [{ transform: 'translateX(0)' }, { transform: 'translateX(-426px)', offset: .45 }, { transform: 'translateX(-426px)', offset: .6 }, { transform: 'translateX(-170px)' }], { easing: 'ease-in-out' });
  const hl2 = add(s, `<div class="headline2" style="color:#E6EDF5">背景透明度 <span class="green">50%–100%</span> 随你调</div>`);
  riseIn(hl2, 49.8);
})();

// =====================================================================
// S12  52–56.2  两个核心卖点
// =====================================================================
(function () {
  const s = scene(52.0, 56.25, 's12');
  const panel = add(s, panelHtml({ rows: [['XLSX', '报表.xlsx', '5 分钟 · 财务'], ['PDF', '合同.pdf', '8 分钟 · 客户资料'], ['PPTX', '季度汇报.pptx', '刚刚 · 财务']] }), { left: '640px', top: '190px' });
  const rRows = rectOf(panel.querySelector('.rows')), rFav = rectOf(panel.querySelector('.sec.fav'));
  popIn(panel, 52.0, .5);
  const r0 = panel.querySelector('.r0'), r1 = panel.querySelector('.r1'), r2 = panel.querySelector('.r2');
  A(r2, 52.7, .55, [{ transform: 'translateY(0)' }, { transform: 'translateY(-172px)' }], { easing: EASE_BACK });
  A(r2, 52.7, 1.2, [{ background: 'rgba(61,220,132,.3)' }, { background: 'rgba(61,220,132,.12)' }]);
  [r0, r1].forEach(r => A(r, 52.7, .55, [{ transform: 'translateY(0)' }, { transform: 'translateY(86px)' }]));
  const z1 = zoneBox(s, rRows, '#3DDC84'); fadeIn(z1, 52.5, .3);
  const c1 = add(s, `<div class="callout" style="right:${1920 - 600}px;top:${rRows.y + rRows.h / 2 - 60}px;color:#3DDC84;font-size:44px;border:2px solid #3DDC8455;line-height:1.35">刚改过的<br>→ 马上找到</div>`);
  A(c1, 52.8, .45, [{ opacity: 0, transform: 'translateX(-40px)' }, { opacity: 1, transform: 'translateX(0)' }]);
  const z2 = zoneBox(s, rFav, '#F2C45C'); fadeIn(z2, 54.0, .3);
  A(panel.querySelectorAll('.tile')[1], 54.2, .5, [{ transform: 'scale(1)' }, { transform: 'scale(1.18)', offset: .5 }, { transform: 'scale(1)' }]);
  const c2 = add(s, `<div class="callout" style="left:1320px;top:${rFav.y + rFav.h / 2 - 60}px;color:#F2C45C;font-size:44px;border:2px solid #F2C45C55;line-height:1.35">常用的<br>→ 一点就到</div>`);
  A(c2, 54.2, .45, [{ opacity: 0, transform: 'translateX(40px)' }, { opacity: 1, transform: 'translateX(0)' }]);
})();

// =====================================================================
// S13  56–60  收尾
// =====================================================================
(function () {
  const s = scene(56.0, 60.6, 's13');
  const card = add(s, `<div class="abs" style="left:856px;top:150px;width:208px;height:192px"></div>`);
  card.appendChild(h(restCard('财务'))); card.firstElementChild.style.cssText = 'left:0;top:0';
  A(card, 56.1, .6, [{ transform: 'scale(.3)', opacity: 0 }, { transform: 'scale(1.25)', opacity: 1 }], { easing: EASE_BACK });
  const wm = add(s, `<div class="headline" style="top:400px;font-size:120px;letter-spacing:3px">Drop<span class="green">Spot</span></div>`);
  popIn(wm, 56.4);
  const tg = add(s, `<div class="headline" style="top:568px;font-size:50px;font-weight:700">刚改的、常用的，都在<span class="green">右下角</span></div>`);
  riseIn(tg, 56.8);
  const sm = add(s, `<div class="headline" style="top:652px;font-size:28px;font-weight:400;color:#8A9BB0">不扫硬盘 · 开机自启 · Windows · v1.0.8</div>`);
  riseIn(sm, 57.1);
  const cta = add(s, `<div class="abs" style="left:780px;top:740px;width:360px;height:84px;border-radius:42px;background:#3DDC84;color:#08240F;font-size:36px;font-weight:700;display:flex;align-items:center;justify-content:center;box-shadow:0 10px 40px rgba(61,220,132,.45)">立即下载试用</div>`);
  popIn(cta, 57.4);
  A(cta, 58.0, 1.0, [{ boxShadow: '0 10px 40px rgba(61,220,132,.45)' }, { boxShadow: '0 10px 70px rgba(61,220,132,.9)' }, { boxShadow: '0 10px 40px rgba(61,220,132,.45)' }], { iterations: 2 });
  // 小人端着咖啡，一身轻松
  const p = add(s, personSvg('p2'), { left: '170px', top: '560px' });
  riseIn(p, 56.5, .6, 60);
  const mug = add(s, `<div class="abs" style="left:398px;top:802px;width:56px;height:58px;border-radius:6px 6px 14px 14px;background:#F5F7FB;border:4px solid #C9D3E0"><div style="position:absolute;right:-22px;top:12px;width:20px;height:26px;border:5px solid #C9D3E0;border-left:none;border-radius:0 12px 12px 0"></div></div>`);
  riseIn(mug, 56.7, .6, 60);
  const steam = add(s, `<div class="abs" style="left:410px;top:740px;font-size:44px;color:#C9D3E0">~</div>`);
  A(steam, 57.3, 1.4, [{ opacity: 0, transform: 'translateY(10px)' }, { opacity: .8, offset: .4 }, { opacity: 0, transform: 'translateY(-30px)' }], { iterations: 2 });
  A(p.querySelector('.armR'), 56.9, .5, [{ transform: 'rotate(0)' }, { transform: 'rotate(-8deg)' }]);
})();
