function rectOf(el) { const r = el.getBoundingClientRect(); return { x: r.left, y: r.top, w: r.width, h: r.height }; }
function zoneBox(parent, r, color, pad = 8) {
  return add(parent, `<div class="zone" style="left:${r.x - pad}px;top:${r.y - pad}px;width:${r.w + pad * 2}px;height:${r.h + pad * 2}px;border-color:${color};box-shadow:0 0 30px ${color}66, inset 0 0 30px ${color}22"></div>`);
}
function ripple(parent, x, y, t) {
  const r = add(parent, `<div class="ripple"></div>`, { left: (x - 20) + 'px', top: (y - 20) + 'px' });
  A(r, t, .5, [{ transform: 'scale(.3)', opacity: 0 }, { transform: 'scale(.5)', opacity: 1, offset: .05 }, { transform: 'scale(2.6)', opacity: 0 }], { easing: 'ease-out' });
}
function explorerHtml(w, h, rows, sel, title = '财务') {
  return `<div class="win" style="width:${w}px;height:${h}px">
    <div class="tb">${folderSvg('fold')}<span>${title}</span><div class="ctl"><span>—</span><span>□</span><span>✕</span></div></div>
    <div class="addr">此电脑 › 工作 (D:) › ${title}</div>
    <div class="nav">★ 快速访问<br>桌面<br>下载<br>文档<br>工作 (D:)</div>
    <div class="flist"><div class="h"><div class="c1">名称</div><div class="c2">修改日期</div><div class="c3">类型</div></div>
    ${rows.map((r, i) => `<div class="r ${i === sel ? 'sel' : ''}"><div class="c1"><div class="mini" style="background:${EXT[r[0]]}"></div>${r[1]}</div><div class="c2">${r[2]}</div><div class="c3">${r[3]}</div></div>`).join('')}
    </div></div>`;
}

// =====================================================================
// S4  8–13.2  认识悬浮舱
// =====================================================================
(function () {
  const s = scene(8.0, 13.25, 's4');
  const title = add(s, `<div class="headline" style="font-size:96px;top:96px;letter-spacing:2px">Drop<span class="green">Spot</span></div>`);
  const sub = add(s, `<div class="headline2" style="top:226px">哪里有新文件，它最先知道</div>`);
  popIn(title, 8.25); riseIn(sub, 8.6);
  const cx = 960, cy = 600;
  const svg = add(s, `<svg class="abs" width="1920" height="1080" style="left:0;top:0"></svg>`);
  const names = ['下载', '桌面', '财务', '素材', '文档', '项目'];
  names.forEach((n, i) => {
    const ang = Math.PI * (0.92 + i * 0.232);
    const fx = cx + Math.cos(ang) * 610, fy = cy + Math.sin(ang) * 230 + (i === 0 || i === 5 ? 0 : 0);
    const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
    Object.entries({ x1: fx, y1: fy, x2: cx, y2: cy, stroke: '#3DDC84', 'stroke-width': 3, 'stroke-dasharray': '10 14', opacity: .55 }).forEach(([k, v]) => line.setAttribute(k, v));
    svg.appendChild(line);
    A(line, 8.6 + i * .12, .4, [{ opacity: 0 }, { opacity: .55 }]);
    A(line, 8.6, 4.6, [{ strokeDashoffset: 0 }, { strokeDashoffset: -240 }], { easing: 'linear' });
    const f = add(s, `<div class="abs" style="width:150px;text-align:center">${folderSvg()}<div style="font-size:22px;color:#8A9BB0;margin-top:4px">${n}</div></div>`, { left: (fx - 75) + 'px', top: (fy - 50) + 'px' });
    f.querySelector('.fold').style.cssText = 'width:84px;height:70px';
    popIn(f, 8.5 + i * .12);
    for (let k = 0; k < 2; k++) {
      const p = add(s, `<div class="abs" style="width:14px;height:14px;border-radius:50%;background:#3DDC84;box-shadow:0 0 16px #3DDC84;left:0;top:0"></div>`);
      A(p, 9.2 + i * .37 + k * 1.9, 1.0, [{ transform: `translate(${fx - 7}px,${fy - 7}px)`, opacity: 0 }, { opacity: 1, offset: .2 }, { transform: `translate(${cx - 7}px,${cy - 7}px)`, opacity: 0 }], { easing: 'ease-in' });
    }
  });
  const wrap = add(s, `<div class="abs" style="left:${cx - 104}px;top:${cy - 96}px;width:208px;height:192px"></div>`);
  wrap.appendChild(h(restCard('财务')));
  wrap.firstElementChild.style.cssText = 'left:0;top:0';
  A(wrap, 8.3, .7, [{ transform: 'scale(.2)', opacity: 0 }, { transform: 'scale(1.5)', opacity: 1 }], { easing: EASE_BACK });
  A(wrap.querySelector('.dot'), 9, .9, [{ boxShadow: '0 0 4px #3DDC84' }, { boxShadow: '0 0 24px #3DDC84' }, { boxShadow: '0 0 4px #3DDC84' }], { iterations: 5 });
})();

// =====================================================================
// S5  13–19.2  新文件提醒 + 双击定位
// =====================================================================
(function () {
  const s = scene(13.0, 19.25, 's5');
  const fin = add(s, `<div class="abs" style="left:250px;top:330px;width:240px;text-align:center">${folderSvg()}<div style="font-size:30px;font-weight:700;margin-top:6px">财务</div></div>`);
  fin.querySelector('.fold').style.cssText = 'width:200px;height:168px';
  popIn(fin, 13.05);
  const xl = add(s, `<div class="abs">${fileSvg('XLSX')}</div>`, { left: '0', top: '0' });
  A(xl, 13.25, .7, [{ transform: 'translate(-120px,80px) rotate(-30deg) scale(1)', opacity: 1 }, { transform: 'translate(180px,180px) rotate(-10deg) scale(1)', opacity: 1, offset: .5 }, { transform: 'translate(330px,360px) rotate(15deg) scale(.2)', opacity: 0 }], { easing: EASE_IO });
  A(fin.querySelector('.fold'), 13.9, .35, [{ transform: 'scale(1)' }, { transform: 'scale(1.12,.9)', offset: .4 }, { transform: 'scale(1)' }]);
  const rest = add(s, restCard('财务'), { left: '1096px', top: '330px' });
  const toast = add(s, toastCard('报表.xlsx', '财务'), { left: '760px', top: '330px', transformOrigin: 'right center' });
  A(rest, 13.95, .25, [{ opacity: 1 }, { opacity: 0 }]);
  A(toast, 13.95, .5, [{ transform: 'scaleX(.38)', opacity: 0 }, { transform: 'scaleX(1)', opacity: 1 }], { easing: EASE_BACK });
  const hl0 = add(s, `<div class="headline">新文件 <span class="green">一保存就提醒</span></div>`);
  riseIn(hl0, 14.1); fadeOut(hl0, 15.9);
  const hand = add(s, `<div class="abs">${handSvg()}</div>`, { left: '0', top: '0' });
  A(hand, 14.6, .7, [{ transform: 'translate(1150px,980px)', opacity: 0 }, { transform: 'translate(1000px,430px)', opacity: 1 }]);
  A(hand, 15.35, .15, [{ transform: 'translate(1000px,430px) scale(1)' }, { transform: 'translate(1000px,430px) scale(.9)' }, { transform: 'translate(1000px,430px) scale(1)' }]);
  A(hand, 15.6, .15, [{ transform: 'translate(1000px,430px) scale(1)' }, { transform: 'translate(1000px,430px) scale(.9)' }, { transform: 'translate(1000px,430px) scale(1)' }]);
  ripple(s, 1022, 440, 15.4); ripple(s, 1022, 440, 15.65);
  [fin, toast, hand].forEach(e => fadeOut(e, 15.95, .3));
  const ex = add(s, explorerHtml(1100, 600, [
    ['XLSX', '报表.xlsx', '2026/10/1 09:41', 'Excel 工作表'],
    ['XLSX', '预算.xlsx', '2026/9/28 17:02', 'Excel 工作表'],
    ['PDF', '发票汇总.pdf', '2026/9/27 11:20', 'PDF 文件'],
    ['DOCX', '会议纪要.docx', '2026/9/25 15:48', 'Word 文档'],
    ['XLSX', '旧版报表.xlsx', '2026/9/20 10:05', 'Excel 工作表']], 0), { left: '410px', top: '250px' });
  A(ex, 16.0, .55, [{ transform: 'translateY(120px) scale(.92)', opacity: 0 }, { transform: 'translateY(0) scale(1)', opacity: 1 }], { easing: EASE_BACK });
  const sel = ex.querySelector('.r.sel');
  A(sel, 16.6, .5, [{ background: 'rgba(61,220,132,0)', boxShadow: '0 0 0 0 rgba(61,220,132,0)' }, { background: 'rgba(61,220,132,.18)', boxShadow: '0 0 0 3px #3DDC84' }]);
  const hl = add(s, `<div class="headline"><span class="green">双击</span>：打开文件夹 + 选中文件</div>`);
  riseIn(hl, 16.2);
})();

// =====================================================================
// S6  19–26.2  悬停展开面板
// =====================================================================
(function () {
  const s = scene(19.0, 26.25, 's6');
  const panel = add(s, panelHtml(), { left: '1120px', top: '210px', transformOrigin: 'top right' });
  const R = q => rectOf(panel.querySelector(q));
  const rRows = R('.rows'), rAct = R('.sec.act'), rFav = R('.sec.fav');
  const card = add(s, restCard('财务'), { left: (1120 + 640 - 208) + 'px', top: '210px' });
  const cur = add(s, `<div class="abs">${cursorSvg()}</div>`, { left: '0', top: '0' });
  A(cur, 19.3, .6, [{ transform: 'translate(1380px,980px)', opacity: 0 }, { transform: 'translate(1650px,300px)', opacity: 1 }]);
  A(card, 19.95, .2, [{ opacity: 1 }, { opacity: 0 }]);
  A(panel, 19.95, .45, [{ transform: 'scale(.33)', opacity: 0 }, { transform: 'scale(1)', opacity: 1 }], { easing: EASE_BACK });
  A(cur, 20.5, .5, [{ transform: 'translate(1650px,300px)' }, { transform: 'translate(1500px,420px)' }]);
  const items = [[rRows, '#3DDC84', '最新文件', 21.0], [rAct, '#6EA8FE', '活跃文件夹', 21.7], [rFav, '#F2C45C', '收藏', 22.4]];
  const extras = [];
  items.forEach(([r, c, label, t]) => {
    const z = zoneBox(s, r, c); fadeIn(z, t, .3); extras.push(z);
    const cy = r.y + r.h / 2;
    const line = add(s, `<div class="abs" style="left:870px;top:${cy - 2}px;width:${r.x - 878 - 8}px;height:4px;border-radius:2px;background:${c};transform-origin:right center"></div>`);
    A(line, t + .1, .35, [{ transform: 'scaleX(0)' }, { transform: 'scaleX(1)' }]);
    const co = add(s, `<div class="callout" style="color:${c};border:2px solid ${c}55;right:${1920 - 862}px;top:${cy - 32}px">${label}</div>`);
    A(co, t + .25, .45, [{ opacity: 0, transform: 'translateX(-30px)' }, { opacity: 1, transform: 'translateX(0)' }]);
    extras.push(line, co);
  });
  const hl = add(s, `<div class="headline">移上去<span class="green">展开</span>，移开就<span class="green">收</span></div>`);
  riseIn(hl, 23.1);
  extras.forEach(e => fadeOut(e, 24.3, .3));
  A(cur, 24.45, .4, [{ transform: 'translate(1500px,420px)', opacity: 1 }, { transform: 'translate(1250px,1000px)', opacity: 0 }]);
  A(panel, 24.85, .35, [{ transform: 'scale(1)', opacity: 1 }, { transform: 'scale(.33)', opacity: 0 }], { easing: EASE_IN });
  A(card, 25.1, .3, [{ opacity: 0 }, { opacity: 1 }]);
})();

// =====================================================================
// S7  26–31.2  拖出去直接发
// =====================================================================
(function () {
  const s = scene(26.0, 31.25, 's7');
  const panel = add(s, panelHtml(), { left: '170px', top: '220px' });
  const r0 = rectOf(panel.querySelector('.r0'));
  const chat = add(s, `<div class="win" style="left:1080px;top:220px;width:680px;height:620px;background:#EEF1F6">
     <div class="tb" style="background:#fff"><div style="width:40px;height:40px;border-radius:50%;background:#F59E6B;display:flex;align-items:center;justify-content:center;font-size:15px;color:#3A1C0C">老板</div>老板</div>
     <div class="abs" style="left:24px;top:84px;padding:14px 22px;border-radius:6px 20px 20px 20px;background:#fff;font-size:24px">把刚才那份报表发我</div>
     <div class="abs fb" style="right:24px;top:180px;width:330px;height:110px;border-radius:20px 6px 20px 20px;background:#fff;display:flex;align-items:center;gap:16px;padding:0 20px;border:3px solid #3DDC84">
        ${fileSvg('XLSX', 56)}<div><div style="font-size:24px;font-weight:700">报表.xlsx</div><div style="font-size:18px;color:#7A8799">48 KB</div></div></div>
     <div class="abs ok" style="left:24px;top:320px;padding:14px 22px;border-radius:6px 20px 20px 20px;background:#fff;font-size:28px;font-weight:700">收到！</div>
     <div class="abs" style="left:0;right:0;bottom:0;height:130px;background:#fff;border-top:1px solid #DCE3EC"></div></div>`);
  popIn(chat, 26.15, .55);
  const fb = chat.querySelector('.fb'), ok = chat.querySelector('.ok');
  const ghost = add(s, `<div class="cap" style="left:0;top:0;width:${r0.w}px;height:84px;border-radius:16px;display:flex;align-items:center;gap:18px;padding:0 10px;border-color:#3DDC84">${badge('XLSX')}<div style="font-size:26px;font-weight:700">报表.xlsx</div></div>`);
  const cur = add(s, `<div class="abs">${cursorSvg()}</div>`, { left: '0', top: '0' });
  const p0 = [r0.x, r0.y], p1 = [r0.x + 380, r0.y - 170], p2 = [1340, 420];
  A(cur, 26.4, .4, [{ transform: `translate(${r0.x + 260}px,${r0.y + 260}px)`, opacity: 0 }, { transform: `translate(${r0.x + 200}px,${r0.y + 40}px)`, opacity: 1 }]);
  A(ghost, 26.85, .25, [{ transform: `translate(${p0[0]}px,${p0[1]}px) scale(1)`, opacity: 0 }, { transform: `translate(${p0[0]}px,${p0[1] - 10}px) scale(1.05)`, opacity: 1 }]);
  A(ghost, 27.15, 1.1, [{ transform: `translate(${p0[0]}px,${p0[1] - 10}px) rotate(0) scale(1.05)` }, { transform: `translate(${p1[0]}px,${p1[1]}px) rotate(-6deg) scale(.95)`, offset: .45 }, { transform: `translate(${p2[0]}px,${p2[1]}px) rotate(3deg) scale(.75)` }], { easing: EASE_IO });
  A(cur, 27.15, 1.1, [{ transform: `translate(${p0[0] + 200}px,${p0[1] + 30}px)` }, { transform: `translate(${p1[0] + 190}px,${p1[1] + 30}px)`, offset: .45 }, { transform: `translate(${p2[0] + 150}px,${p2[1] + 30}px)` }], { easing: EASE_IO });
  A(ghost, 28.3, .2, [{ opacity: 1 }, { opacity: 0 }]);
  A(fb, 28.35, .45, [{ opacity: 0, transform: 'scale(.6)' }, { opacity: 1, transform: 'scale(1)' }], { easing: EASE_BACK });
  A(ok, 29.0, .45, [{ opacity: 0, transform: 'scale(.6)' }, { opacity: 1, transform: 'scale(1)' }], { easing: EASE_BACK });
  fadeOut(cur, 28.6, .3);
  const hl = add(s, `<div class="headline">拖进<span class="green">微信 / 邮件</span>，直接发</div>`);
  riseIn(hl, 26.7);
})();

// =====================================================================
// S8  31–37.2  拖进来收藏
// =====================================================================
(function () {
  const s = scene(31.0, 37.25, 's8');
  const panel = add(s, panelHtml({ favs: ['客户资料', '合同', '素材图', '周报'], more: 3, stars: true }), { left: '1090px', top: '170px' });
  const t0 = panel.querySelector('.tile.t0'), stars = panel.querySelector('.stars');
  const rt0 = rectOf(t0), rst = rectOf(stars), rp = rectOf(panel);
  A(t0, 32.65, .55, [{ opacity: 0, transform: 'scale(0)' }, { opacity: 1, transform: 'scale(1)' }], { easing: EASE_BACK });
  A(t0, 32.65, 1.2, [{ background: 'rgba(242,196,92,.35)' }, { background: 'rgba(242,196,92,0)' }]);
  A(stars, 34.85, .5, [{ opacity: 0, transform: 'translateX(30px)' }, { opacity: 1, transform: 'translateX(0)' }]);
  A(stars.querySelector('.st'), 34.9, .6, [{ transform: 'scale(0) rotate(-90deg)' }, { transform: 'scale(1.6) rotate(10deg)', offset: .6 }, { transform: 'scale(1) rotate(0)' }], { easing: EASE_BACK });
  // 投放区
  const drop = add(s, `<div class="abs" style="left:${rp.x}px;top:${rp.y}px;width:${rp.w}px;height:${rp.h}px;border-radius:32px;border:4px dashed #F2C45C;background:rgba(36,30,14,.94);display:flex;flex-direction:column;align-items:center;justify-content:center;gap:20px">
     ${folderSvg()}<div style="font-size:40px;font-weight:700;color:#F2C45C">松手即可收藏</div><div style="font-size:24px;color:#B9A16A">文件夹加入收藏 · 文件打上 ★</div></div>`);
  drop.querySelector('.fold').style.cssText = 'width:130px;height:108px;opacity:.5';
  A(drop, 31.8, .3, [{ opacity: 0 }, { opacity: 1 }]);
  A(drop, 32.55, .25, [{ opacity: 1 }, { opacity: 0 }]);
  // 金色文件夹飞进来
  const fd = add(s, `<div class="abs" style="width:170px;text-align:center;left:0;top:0">${folderSvg()}<div style="font-size:24px;font-weight:700">客户资料</div></div>`);
  fd.querySelector('.fold').style.cssText = 'width:150px;height:125px';
  const cxp = rp.x + rp.w / 2 - 85, cyp = rp.y + rp.h / 2 - 250;
  A(fd, 31.25, 1.05, [{ transform: 'translate(120px,420px) rotate(-10deg) scale(1)', opacity: 0 }, { opacity: 1, offset: .15 }, { transform: `translate(${cxp}px,${cyp}px) rotate(4deg) scale(1)`, opacity: 1 }], { easing: EASE_IO });
  A(fd, 32.4, .35, [{ transform: `translate(${cxp}px,${cyp}px) scale(1)`, opacity: 1 }, { transform: `translate(${rt0.x + rt0.w / 2 - 85}px,${rt0.y - 40}px) scale(.3)`, opacity: 0 }], { easing: EASE_IN });
  // 合同.pdf 飞进来，打上 ★
  const pdf = add(s, `<div class="abs">${fileSvg('PDF')}</div>`, { left: '0', top: '0' });
  A(pdf, 33.95, .85, [{ transform: 'translate(160px,760px) rotate(-20deg) scale(1)', opacity: 0 }, { opacity: 1, offset: .15 }, { transform: `translate(${rp.x + 200}px,${rp.y + 260}px) rotate(5deg) scale(1)`, opacity: 1, offset: .75 }, { transform: `translate(${rst.x + 10}px,${rst.y - 30}px) scale(.25)`, opacity: 0 }], { easing: EASE_IO });
  const l1 = add(s, `<div class="lbl" style="left:150px;top:380px">文件夹拖进来 = <span class="gold">收藏</span></div>`);
  const l2 = add(s, `<div class="lbl" style="left:150px;top:500px">文件拖进来 = <span class="gold">★ 标记</span></div>`);
  riseIn(l1, 32.7); riseIn(l2, 35.0);
  const hl = add(s, `<div class="headline" style="text-align:left;left:150px">常用的，<span class="gold">拖进来</span>就收藏</div>`);
  riseIn(hl, 31.4);
})();
